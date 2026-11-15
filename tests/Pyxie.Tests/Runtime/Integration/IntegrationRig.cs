using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Integration;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

/// <summary>A <see cref="CardIntegrator"/> over a real hub: a bare clone of a source repository, set up the way an import sets it up (<see cref="GitClient.CloneBareAsync"/> and then <see cref="GitClient.SetUpHubAsync"/>, hooks included), so the rig meets the same refusals the pipeline does. The branch the source had checked out is the target branch.</summary>
/// <remarks>Card branches reach the hub the way take-back puts them there: a worker commits in a clone of its own and the hub fetches the card branch from it, because the hub refuses pushes to <c>pyxie/</c> branches. A person moves the target branch from a clone of their own, with a push the hub's <c>update</c> hook accepts. Shape the source before creating the rig, since the hub is cloned from it once, and declare the source first so it is deleted last.</remarks>
public sealed class IntegrationRig : IAsyncDisposable
{
	// A first merge round's place in a card's history: work, gates, review, merge.
	private const int MergeSequence = 4;

	private readonly string _folder;
	private readonly Repository _repository;
	private readonly CardIntegrator _integrator;

	private IntegrationRig(string folder, string origin, TempGitRepository hub, ClonedRepository person, ClonedRepository worker, string targetBranch)
	{
		_folder = folder;
		Hub = hub;
		Person = person;
		Worker = worker;
		TargetBranch = targetBranch;
		_repository = new Repository { Source = RepositorySource.Folder, Path = hub.Folder, Origin = origin, TargetBranch = targetBranch, ImportedUtc = DateTime.UtcNow };
		RepositoryLock = new RepositoryLock();
		_integrator = new CardIntegrator(hub.Git, RepositoryLock);
	}

	/// <summary>Gets the hub, a bare repository, which is the only repository the integrator writes to.</summary>
	public TempGitRepository Hub { get; }

	/// <summary>Gets a person's clone of the hub, which moves the target branch with pushes.</summary>
	public ClonedRepository Person { get; }

	/// <summary>Gets a worker's clone of the hub, standing in for the repository in a card's worker sandbox. Nothing in it reaches the hub until <see cref="HandBackAsync"/>.</summary>
	public ClonedRepository Worker { get; }

	/// <summary>Gets the target branch: the branch the source had checked out.</summary>
	public string TargetBranch { get; }

	/// <summary>Gets the lock the integrator serializes its integrations with.</summary>
	public RepositoryLock RepositoryLock { get; }

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Imports a source the way Pyxie does: clones it into a bare hub, sets the hub up with the source's checked-out branch as the target branch, and clones the hub twice, for a person and a worker.</summary>
	/// <param name="source">The repository to import, already shaped as the test needs; the hub does not see later changes to it.</param>
	/// <returns>The rig.</returns>
	public static async Task<IntegrationRig> CreateAsync(TempGitRepository source)
	{
		string folder = TempFolder.Create("pyxie-int");
		TempGitRepository? hub = null;
		ClonedRepository? person = null;

		try
		{
			string hubFolder = Path.Combine(folder, "hub");
			ProcessResult clone = await source.Git.CloneBareAsync(source.Folder, hubFolder, Token);
			Assert.True(clone.Succeeded, $"The source could not be cloned into a hub: {clone.StartError ?? clone.Output}");
			string targetBranch = await source.Git.GetCurrentBranchAsync(hubFolder, Token) ?? throw new InvalidOperationException("The hub has no branch checked out.");
			await source.Git.SetUpHubAsync(hubFolder, targetBranch, Token);

			hub = await TempGitRepository.AttachAsync(hubFolder);
			person = await ClonedRepository.CreateAsync(hub);
			ClonedRepository worker = await ClonedRepository.CreateAsync(hub);

			return new IntegrationRig(folder, source.Folder, hub, person, worker, targetBranch);
		}
		catch
		{
			if (person is not null)
			{
				await person.DisposeAsync();
			}

			if (hub is not null)
			{
				await hub.DisposeAsync();
			}

			await TempFolder.DeleteAsync(folder);

			throw;
		}
	}

	/// <summary>Runs a merge round for a card, as the pipeline opens it: named <c>Card</c> and the identifier, with no agent and no briefing.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="sequence">The round's place in the card's history.</param>
	/// <param name="descriptions">What the cards that landed on the target branch were for, by card; no card is known unless given.</param>
	/// <returns>What the integration came to.</returns>
	public Task<RoundVerdict> IntegrateAsync(int cardId, int sequence = MergeSequence, IReadOnlyDictionary<int, string>? descriptions = null)
	{
		PipelineWork work = new(cardId, $"Card {cardId}", RoundId: sequence, Sequence: sequence, RoundKind.Merge, Attempt: 1, Agent: null, Briefing: null);

		return _integrator.IntegrateAsync(_repository, work, (landed, _) => Task.FromResult(descriptions is not null && descriptions.TryGetValue(landed, out string? description) ? description : null), Token);
	}

	/// <summary>Starts a card whose first work round changed nothing: its branch is created in the hub at the target branch's tip, as take-back leaves it.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The card branch's tip, which is the target branch's.</returns>
	public async Task<string> StartEmptyCardAsync(int cardId)
	{
		await Hub.RunGitAsync("branch", PipelineContract.BranchName(cardId), $"refs/heads/{TargetBranch}");

		return await TipAsync(PipelineContract.BranchName(cardId));
	}

	/// <summary>Runs a work round that commits one file: the worker starts where the hub's card branch is, or at the target branch's tip for a card that has none yet, and the commit comes back into the hub.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="relativePath">The file the worker adds or changes.</param>
	/// <param name="content">The file's text.</param>
	/// <returns>The card branch's tip in the hub.</returns>
	public async Task<string> CommitOnCardAsync(int cardId, string relativePath, string content)
	{
		await PrepareWorkerAsync(cardId);
		string tip = await Worker.CommitFileAsync(relativePath, content);
		await HandBackAsync(cardId);

		return tip;
	}

	/// <summary>Hands a card in to the worker, as a work round's hand-in does: the worker's clone is brought up to date with the hub, the card branch is checked out where the hub has it (or at the target branch's tip when the hub has none), and a local target branch is set to the hub's, so <c>git merge</c> with the target's plain name works as it does in a sandbox.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>A task that completes when the worker is on the card branch.</returns>
	public async Task PrepareWorkerAsync(int cardId)
	{
		string branch = PipelineContract.BranchName(cardId);
		await Worker.FetchAsync();
		string start = await Hub.Git.ResolveCommitAsync(Hub.Folder, $"refs/heads/{branch}", Token) is null ? $"origin/{TargetBranch}" : $"origin/{branch}";
		await Worker.RunGitAsync("checkout", "--quiet", "-B", branch, start);
		await Worker.RunGitAsync("branch", "--force", TargetBranch, $"origin/{TargetBranch}");
	}

	/// <summary>Takes the worker's card branch back into the hub, the way take-back does: the hub fetches it, only as a fast-forward of what the hub has.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="destination">The hub ref to write; the card branch unless given, or a ref of the test's own to have the commits in the hub without moving the card branch.</param>
	/// <returns>A task that completes when the hub has the commits.</returns>
	public async Task HandBackAsync(int cardId, string? destination = null)
	{
		string branch = $"refs/heads/{PipelineContract.BranchName(cardId)}";
		await Hub.RunGitAsync("fetch", "--quiet", "--no-tags", Worker.Folder, $"{branch}:{destination ?? branch}");
	}

	/// <summary>Brings the person's clone up to date with the hub and checks out the target branch where the hub has it, as a person does before committing.</summary>
	/// <returns>A task that completes when the person is on the target branch.</returns>
	public async Task UpdatePersonAsync()
	{
		await Person.FetchAsync();
		await Person.RunGitAsync("checkout", "--quiet", "-B", TargetBranch, $"origin/{TargetBranch}");
	}

	/// <summary>Moves the target branch on the way a person does: commits one file in the person's clone on top of the hub's target branch and pushes it.</summary>
	/// <param name="relativePath">The file the person adds or changes.</param>
	/// <param name="content">The file's text.</param>
	/// <returns>The target branch's new tip.</returns>
	public async Task<string> PushToTargetAsync(string relativePath, string content)
	{
		await UpdatePersonAsync();
		string tip = await Person.CommitFileAsync(relativePath, content);
		await Person.PushAsync(TargetBranch);

		return tip;
	}

	/// <summary>Gets a branch's tip in the hub.</summary>
	/// <param name="branch">The branch name, without <c>refs/heads/</c>.</param>
	/// <returns>The commit. The test fails when the branch does not exist.</returns>
	public async Task<string> TipAsync(string branch)
	{
		return await Hub.GetHashAsync($"refs/heads/{branch}");
	}

	/// <summary>Gets a commit's parents in the hub.</summary>
	/// <param name="revision">The commit or branch.</param>
	/// <returns>The parent hashes, space-separated, in order.</returns>
	public async Task<string> ParentsAsync(string revision)
	{
		return await Hub.RunGitAsync("log", "-1", "--format=%P", revision);
	}

	/// <summary>Gets a commit's subject in the hub.</summary>
	/// <param name="revision">The commit or branch.</param>
	/// <returns>The subject line.</returns>
	public async Task<string> SubjectAsync(string revision)
	{
		return await Hub.RunGitAsync("log", "-1", "--format=%s", revision);
	}

	/// <summary>Gets a commit's tree in the hub.</summary>
	/// <param name="revision">The commit or branch.</param>
	/// <returns>The tree hash.</returns>
	public async Task<string> TreeAsync(string revision)
	{
		return await Hub.GetHashAsync($"{revision}^{{tree}}");
	}

	/// <summary>Lists every ref in the hub with the object it points at, to show what an integration moved, added, or left alone.</summary>
	/// <returns>One <c>refname object</c> line per ref, sorted by name.</returns>
	public async Task<IReadOnlyList<string>> ListRefsAsync()
	{
		string output = await Hub.RunGitAsync("for-each-ref", "--format=%(refname) %(objectname)");

		return output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>Merges two commits the ordinary way, with <c>git merge</c> in the worker's clone, for comparing the tree a merge without a checkout wrote with the one git's own merge writes.</summary>
	/// <param name="ours">The commit merged into.</param>
	/// <param name="theirs">The commit merged in.</param>
	/// <returns>The merged tree. The test fails when the merge does not succeed cleanly.</returns>
	public async Task<string> MergeInWorkerAsync(string ours, string theirs)
	{
		await Worker.FetchAsync();
		await Worker.RunGitAsync("checkout", "--quiet", "--detach", ours);
		await Worker.RunGitAsync("merge", "--quiet", "--no-edit", theirs);

		return await Worker.RunGitAsync("rev-parse", "HEAD^{tree}");
	}

	/// <summary>Deletes the clones and the hub, and releases the lock.</summary>
	/// <returns>A task that completes when everything the rig created is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		await Worker.DisposeAsync();
		await Person.DisposeAsync();
		await Hub.DisposeAsync();
		RepositoryLock.Dispose();
		await TempFolder.DeleteAsync(_folder);
	}
}
