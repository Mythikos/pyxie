using System.Diagnostics;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary><see cref="CardSandboxes"/> over the local sandbox stand-in (<see cref="LocalSandboxes"/>) and a real hub: a bare clone of a <see cref="TempGitRepository"/>, set up the way an import sets one up, in a project folder of the rig's own. Nothing is stored in a database, because <see cref="CardSandboxes"/> only reads the round context it is given. A sandbox's repository is a plain folder on the host here, so a test plays the agent by writing files and running git in it. Declare the source repository first, so it is deleted last.</summary>
public sealed class CardSandboxRig : IAsyncDisposable
{
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(50);

	private readonly ScratchFolder _root;
	private readonly ProcessRunner _runner;

	private CardSandboxRig(ScratchFolder root, TempGitRepository source, TempGitRepository hub, string projectFolder, string targetBranch, ProcessRunner runner, ISandboxSignIns signIns, Func<ISandboxes, ISandboxes>? decorate)
	{
		_root = root;
		_runner = runner;
		Source = source;
		Hub = hub;
		ProjectFolder = projectFolder;
		ProjectKey = SandboxNames.ProjectKey(projectFolder);
		Git = new GitClient(runner);
		RepositoryLock = new RepositoryLock();
		Sandboxes = new LocalSandboxes(runner, root.Combine("engine"));
		Engine = decorate?.Invoke(Sandboxes) ?? Sandboxes;
		Cards = new CardSandboxes(Engine, signIns, Git, RepositoryLock);
		Repository = new Repository { Id = 1, Source = RepositorySource.Folder, Path = hub.Folder, Origin = source.Folder, TargetBranch = targetBranch };
		Context = new CardRoundContext(projectFolder, ProjectKey, Repository, SandboxDefaults.Dockerfile);
	}

	/// <summary>Gets the repository the hub was cloned from.</summary>
	public TempGitRepository Source { get; }

	/// <summary>Gets the hub: the bare repository Pyxie hands cards out of and takes their work back into.</summary>
	public TempGitRepository Hub { get; }

	/// <summary>Gets the project folder, which holds the hub and the host side of the card sandboxes.</summary>
	public string ProjectFolder { get; }

	/// <summary>Gets the project's key, as <see cref="CardSandboxes"/> works it out from <see cref="ProjectFolder"/>.</summary>
	public string ProjectKey { get; }

	/// <summary>Gets the git client the card sandboxes use on the hub.</summary>
	public GitClient Git { get; }

	/// <summary>Gets the lock that serializes moves of the hub's branches.</summary>
	public RepositoryLock RepositoryLock { get; }

	/// <summary>Gets the local stand-in that runs the sandboxes.</summary>
	public LocalSandboxes Sandboxes { get; }

	/// <summary>Gets the sandboxes the card sandboxes were given: <see cref="Sandboxes"/>, or what the test wrapped it in.</summary>
	public ISandboxes Engine { get; }

	/// <summary>Gets the card sandboxes under test.</summary>
	public CardSandboxes Cards { get; }

	/// <summary>Gets the project's repository record: the hub and its target branch.</summary>
	public Repository Repository { get; }

	/// <summary>Gets the round context every round of the rig runs with.</summary>
	public CardRoundContext Context { get; }

	/// <summary>Gets the target branch.</summary>
	public string TargetBranch => Repository.TargetBranch;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Clones a hub from a repository and builds the card sandboxes over it.</summary>
	/// <param name="source">The repository the hub is cloned from.</param>
	/// <param name="targetBranch">The branch card branches start from.</param>
	/// <param name="signIns">What an agent's sandbox gets to sign in; nothing unless given.</param>
	/// <param name="decorate">Wraps the local stand-in, for a test that changes what the sandboxes answer.</param>
	/// <returns>The rig.</returns>
	public static async Task<CardSandboxRig> CreateAsync(TempGitRepository source, string targetBranch = TempGitRepository.MainBranch, ISandboxSignIns? signIns = null, Func<ISandboxes, ISandboxes>? decorate = null)
	{
		ScratchFolder root = ScratchFolder.Create("pyxie-cs");
		ProcessRunner runner = new(TimeProvider.System);

		try
		{
			string projectFolder = root.Combine("project");
			string hubPath = WorkspaceLayout.HubPath(projectFolder);
			GitClient git = new(runner);
			ProcessResult clone = await git.CloneBareAsync(source.Folder, hubPath, Token);
			Assert.True(clone.Succeeded, $"The hub could not be cloned: {clone.Output}");
			await git.SetUpHubAsync(hubPath, targetBranch, Token);
			TempGitRepository hub = await TempGitRepository.AttachAsync(hubPath);

			return new CardSandboxRig(root, source, hub, projectFolder, targetBranch, runner, signIns ?? new StandInSignIns(), decorate);
		}
		catch
		{
			runner.Dispose();
			await root.DisposeAsync();

			throw;
		}
	}

	/// <summary>Describes a round the way the pipeline hands one to the card sandboxes.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="sequence">The round's place in the card's history; the first round is 1.</param>
	/// <param name="kind">What the round does.</param>
	/// <param name="agent">The agent that runs it, if any.</param>
	/// <returns>The round.</returns>
	public static PipelineWork Work(int cardId, int sequence, RoundKind kind, AgentSnapshot? agent = null)
	{
		return new PipelineWork(cardId, $"Card {cardId}", RoundId: sequence, sequence, kind, Attempt: 1, agent, Briefing: null);
	}

	/// <summary>Gets a sandbox's repository as a folder on the host, where the stand-in keeps it.</summary>
	/// <param name="card">The opened sandbox.</param>
	/// <returns>The full path.</returns>
	public static string HostPath(CardSandbox card)
	{
		return Path.GetFullPath(card.RepositoryPath);
	}

	/// <summary>Writes a file in a folder, creating its folders. Nothing is staged.</summary>
	/// <param name="folder">The folder, such as a sandbox's repository.</param>
	/// <param name="relativePath">The path under it, with forward slashes.</param>
	/// <param name="content">The text, without a final line break so no line-ending setting changes it.</param>
	/// <returns>A task that completes when the file is written.</returns>
	public static async Task WriteAsync(string folder, string relativePath, string content)
	{
		string path = Path.GetFullPath(relativePath, folder);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? folder);
		await File.WriteAllTextAsync(path, content, Token);
	}

	/// <summary>Reads a file in a folder.</summary>
	/// <param name="folder">The folder, such as a sandbox's repository.</param>
	/// <param name="relativePath">The path under it, with forward slashes.</param>
	/// <returns>The text, or <see langword="null"/> when there is no such file.</returns>
	public static async Task<string?> ReadAsync(string folder, string relativePath)
	{
		string path = Path.GetFullPath(relativePath, folder);

		return File.Exists(path) ? await File.ReadAllTextAsync(path, Token) : null;
	}

	/// <summary>Opens a card's sandbox for a round.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="sequence">The round's place in the card's history; the first round is 1.</param>
	/// <param name="kind">What the round does.</param>
	/// <param name="agent">The agent that runs it, if any.</param>
	/// <returns>The sandbox, which the caller disposes.</returns>
	public async Task<CardSandbox> OpenAsync(int cardId, int sequence, RoundKind kind, AgentSnapshot? agent = null)
	{
		return await Cards.OpenAsync(Context, Work(cardId, sequence, kind, agent), Token);
	}

	/// <summary>Plays a whole work round: opens the worker's sandbox, writes one file, commits it, and takes it back into the hub. The worker's sandbox is kept.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="sequence">The round's place in the card's history.</param>
	/// <param name="relativePath">The file the worker writes.</param>
	/// <param name="content">Its text.</param>
	/// <returns>The card branch's tip in the hub afterwards.</returns>
	public async Task<string> WorkRoundAsync(int cardId, int sequence, string relativePath, string content)
	{
		await using CardSandbox card = await OpenAsync(cardId, sequence, RoundKind.Work);
		await WriteAsync(HostPath(card), relativePath, content);

		Assert.Null(await Cards.CommitAsync(card, $"Round {sequence}", Token));
		Assert.Null(await Cards.TakeBackAsync(Context, card, Token));

		return await HubCardTipAsync(cardId) ?? throw new InvalidOperationException($"The work round of card {cardId} left no card branch in the hub.");
	}

	/// <summary>Gets where the stand-in keeps a card worker's repository, which outlives the worker's rounds.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The full path.</returns>
	public string WorkerRepository(int cardId)
	{
		return Path.Combine(Sandboxes.WorkFolder(SandboxNames.Container(ProjectKey, cardId, SandboxRole.Worker)), "repo");
	}

	/// <summary>Gets the host folder of one of a card's sandboxes: its input, output, and sign-in.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="role">The sandbox's role.</param>
	/// <returns>The full path.</returns>
	public string SandboxFolder(int cardId, SandboxRole role)
	{
		return WorkspaceLayout.SandboxFolder(ProjectFolder, cardId, SandboxNames.RoleName(role));
	}

	/// <summary>Runs git in a folder the test owns, such as a sandbox's repository, and fails the test when git does not succeed. Commit signing is off.</summary>
	/// <param name="folder">The folder git runs in.</param>
	/// <param name="arguments">The git arguments.</param>
	/// <returns>What git printed, trimmed.</returns>
	public async Task<string> RunGitInAsync(string folder, params string[] arguments)
	{
		return await Source.RunGitAsync(["-C", folder, "-c", "commit.gpgsign=false", .. arguments]);
	}

	/// <summary>Runs git in a folder the test owns and returns the result without judging it. Commit signing is off.</summary>
	/// <param name="folder">The folder git runs in.</param>
	/// <param name="arguments">The git arguments.</param>
	/// <returns>How the run ended.</returns>
	public async Task<ProcessResult> TryRunGitInAsync(string folder, params string[] arguments)
	{
		return await Source.TryRunGitAsync(["-C", folder, "-c", "commit.gpgsign=false", .. arguments]);
	}

	/// <summary>Writes one file in a sandbox's repository and commits everything changed there, as an agent does.</summary>
	/// <param name="folder">The sandbox's repository on the host.</param>
	/// <param name="relativePath">The file.</param>
	/// <param name="content">Its text.</param>
	/// <returns>The new commit's hash.</returns>
	public async Task<string> CommitInAsync(string folder, string relativePath, string content)
	{
		await WriteAsync(folder, relativePath, content);
		await RunGitInAsync(folder, "add", "--all");
		await RunGitInAsync(folder, "commit", "--quiet", "--message", $"Change {relativePath}");

		return await RunGitInAsync(folder, "rev-parse", "HEAD");
	}

	/// <summary>Gets the commit a revision names in a folder's repository.</summary>
	/// <param name="folder">The repository.</param>
	/// <param name="revision">The revision.</param>
	/// <returns>The full hash.</returns>
	public async Task<string> HashInAsync(string folder, string revision = "HEAD")
	{
		return await RunGitInAsync(folder, "rev-parse", "--verify", revision);
	}

	/// <summary>Gets the branch checked out in a folder's repository.</summary>
	/// <param name="folder">The repository.</param>
	/// <returns>The branch, or <see langword="null"/> when HEAD is detached.</returns>
	public async Task<string?> BranchInAsync(string folder)
	{
		ProcessResult result = await TryRunGitInAsync(folder, "symbolic-ref", "--quiet", "--short", "HEAD");

		return result.Succeeded ? result.StandardOutput.Trim() : null;
	}

	/// <summary>Determines whether a merge is in progress in a folder's repository.</summary>
	/// <param name="folder">The repository.</param>
	/// <returns><see langword="true"/> when the repository has a <c>MERGE_HEAD</c>.</returns>
	public async Task<bool> IsMergingAsync(string folder)
	{
		return (await TryRunGitInAsync(folder, "rev-parse", "--quiet", "--verify", "MERGE_HEAD")).Succeeded;
	}

	/// <summary>Gets the card branch's tip in the hub.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The commit, or <see langword="null"/> when the hub has no branch for the card.</returns>
	public async Task<string?> HubCardTipAsync(int cardId)
	{
		return await Git.ResolveCommitAsync(Hub.Folder, $"refs/heads/{PipelineContract.BranchName(cardId)}", Token);
	}

	/// <summary>Moves the target branch on in the hub the way a person does: clone the hub, commit a file, and push.</summary>
	/// <param name="relativePath">The file to commit.</param>
	/// <param name="content">Its text.</param>
	/// <returns>The target branch's new tip.</returns>
	public async Task<string> MoveTargetAsync(string relativePath, string content)
	{
		await using ClonedRepository clone = await ClonedRepository.CreateAsync(Hub);
		await clone.RunGitAsync("checkout", "--quiet", "-B", TargetBranch, $"origin/{TargetBranch}");
		string tip = await clone.CommitFileAsync(relativePath, content);
		await clone.PushAsync(TargetBranch);

		return tip;
	}

	/// <summary>Makes a card conflict with the target branch: the card's work round changes the repository's first file, and then the target branch moves on with a different change to the same line.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The card branch's tip and the target branch's new tip, the commits a resolve round merges.</returns>
	public async Task<(string CardTip, string TargetTip)> ConflictAsync(int cardId = 1)
	{
		string cardTip = await WorkRoundAsync(cardId, 1, TempGitRepository.InitialFile, "the card's version");
		string targetTip = await MoveTargetAsync(TempGitRepository.InitialFile, "the target's version");

		return (cardTip, targetTip);
	}

	/// <summary>Merges the target branch into a card's branch in the hub the way the integrator does when the target moved: a merge commit on the card branch, by compare-and-swap.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The card branch's new tip.</returns>
	public async Task<string> MergeTargetIntoCardBranchAsync(int cardId)
	{
		string branch = PipelineContract.BranchName(cardId);
		string cardTip = await Hub.GetHashAsync($"refs/heads/{branch}");
		string targetTip = await Hub.GetHashAsync($"refs/heads/{TargetBranch}");
		GitMergeTree merge = await Git.MergeTreeAsync(Hub.Folder, cardTip, targetTip, Token);
		Assert.True(merge.ConflictedFiles.Count == 0, $"The target branch must merge cleanly into {branch}: {string.Join(", ", merge.ConflictedFiles)}");
		string commit = await Git.CommitTreeAsync(Hub.Folder, merge.Tree, [cardTip, targetTip], $"Merge {TargetBranch} into {branch}", Token);
		Assert.True(await Git.TryMoveBranchAsync(Hub.Folder, branch, commit, cardTip, Token), $"{branch} moved while the test merged into it.");

		return commit;
	}

	/// <summary>Lists the refs in the bundle a card's sandbox was last handed.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="role">The sandbox's role.</param>
	/// <returns>The full ref names, in order.</returns>
	public async Task<IReadOnlyList<string>> HandInRefsAsync(int cardId, SandboxRole role)
	{
		string bundle = Path.Combine(SandboxFolder(cardId, role), "in", "card.bundle");
		string heads = await Hub.RunGitAsync("bundle", "list-heads", bundle);

		return [.. heads.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[^1]).Order(StringComparer.Ordinal)];
	}

	/// <summary>Lists the refs Pyxie keeps under <c>refs/pyxie</c> in the hub, where work that comes back is fetched before it is checked.</summary>
	/// <returns>The full ref names; empty when there are none.</returns>
	public async Task<IReadOnlyList<string>> PyxieRefsAsync()
	{
		string refs = await Hub.RunGitAsync("for-each-ref", "--format=%(refname)", "refs/pyxie");

		return refs.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>Waits until a condition holds, polling it, so a test can act at a point another task reaches on its own.</summary>
	/// <param name="condition">The condition.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ...".</param>
	/// <returns>A task that completes when the condition holds. The test fails when it does not within <see cref="RuntimeTestTimeouts.Wait"/>.</returns>
	public static async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!await condition())
		{
			Assert.True(stopwatch.Elapsed < RuntimeTestTimeouts.Wait, $"Timed out waiting for {description}.");
			await Task.Delay(s_pollInterval, Token);
		}
	}

	/// <summary>Releases the lock and the hub's helpers, and deletes the project folder and every sandbox folder.</summary>
	/// <returns>A task that completes when the rig's folders are gone.</returns>
	public async ValueTask DisposeAsync()
	{
		RepositoryLock.Dispose();
		await Hub.DisposeAsync();
		_runner.Dispose();
		await _root.DisposeAsync();
	}
}
