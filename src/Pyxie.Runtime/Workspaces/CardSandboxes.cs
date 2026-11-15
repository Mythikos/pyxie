using System.Globalization;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Runtime.Workspaces;

/// <summary>Runs a card's rounds in sandboxes, the way a pull request comes from a fork (see <c>docs/sandboxing.md</c>). Pyxie hands the card in as a bundle made in the hub; the sandbox's own repository fetches it as <c>origin/*</c> and checks out the card's branch at the commit Pyxie names. A worker's new commits come back as a bundle, which Pyxie verifies, fetches into a ref of its own, checks builds on the commit it handed in, and only then moves the card's branch to; so does a merge agent's resolution, with checks of its own. Pyxie never runs git in a sandbox's repository from the host: everything done to it runs inside the sandbox.</summary>
public sealed class CardSandboxes
{
	private const string RepositoryFolder = "repo";
	private const string HandInFile = "card.bundle";
	private const string HandOutFile = "result.bundle";
	private const string ReceivedFile = "received.bundle";
	private const string SignInFolder = "sign-in";
	private const string WorkVolume = "work";
	private const string HomeVolume = "home";
	private const string GatesHomeVolume = "gates-home";
	private const string HomeInside = "/home/node";
	private const long MaxBundleBytes = 2L * 1024 * 1024 * 1024;

	// A conflicted file larger than this is not searched for conflict markers.
	private const int MaxCheckedFileBytes = 8 * 1024 * 1024;

	private static readonly TimeSpan s_gitTimeout = TimeSpan.FromMinutes(30);

	// The locks git takes in a repository's own folder; ref locks are named after the refs.
	private static readonly string[] s_lockFiles = ["index.lock", "HEAD.lock", "config.lock", "packed-refs.lock", "shallow.lock"];

	private readonly ISandboxes _sandboxes;
	private readonly ISandboxSignIns _signIns;
	private readonly GitClient _git;
	private readonly RepositoryLock _repositoryLock;

	/// <summary>Initializes a new instance of the <see cref="CardSandboxes"/> class.</summary>
	/// <param name="sandboxes">Makes the sandboxes.</param>
	/// <param name="signIns">Decides what an agent's sandbox gets to sign in.</param>
	/// <param name="git">The git client, for the hub.</param>
	/// <param name="repositoryLock">The lock that serializes moves of the hub's branches.</param>
	public CardSandboxes(ISandboxes sandboxes, ISandboxSignIns signIns, GitClient git, RepositoryLock repositoryLock)
	{
		_sandboxes = sandboxes;
		_signIns = signIns;
		_git = git;
		_repositoryLock = repositoryLock;
	}

	/// <summary>Opens the sandbox a work, gate, review, or resolve round runs in, with the card's repository checked out where the round starts. A card's first round starts afresh, so nothing an earlier card with the same number left is reused. A worker's sandbox is kept between the card's work rounds and keeps what an interrupted or broken attempt left, so the worker can carry on from it; gate, review, and resolve rounds get a fresh sandbox on the card's last commit. A reviewer's workspace has the target branch's version of the files that steer a harness (see <see cref="SteeringFiles"/>). A merge agent's workspace is in the middle of the merge Pyxie started: the target branch's commit that was current when the sandbox opened is merged into the card's branch without committing, which leaves the conflicts to resolve.</summary>
	/// <param name="context">The open project.</param>
	/// <param name="work">The round about to run.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The sandbox, ready for the round.</returns>
	/// <exception cref="SandboxException">The project's image could not be built, the agent cannot sign in, the sandbox could not be prepared, or the merge could not be started.</exception>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	/// <exception cref="GitException">The hub could not be read, or the card's branch is missing for a round that needs its work.</exception>
	public async Task<CardSandbox> OpenAsync(CardRoundContext context, PipelineWork work, CancellationToken cancellationToken)
	{
		SandboxRole role = work.Kind switch
		{
			RoundKind.Work => SandboxRole.Worker,
			RoundKind.Gate => SandboxRole.Gates,
			RoundKind.Review => SandboxRole.Review,
			RoundKind.Resolve => SandboxRole.Resolve,
			_ => throw new ArgumentOutOfRangeException(nameof(work), work.Kind, "Only work, gate, review, and resolve rounds run in a sandbox."),
		};

		string hub = context.Repository.Path;
		string target = context.Repository.TargetBranch;
		string branch = PipelineContract.BranchName(work.CardId);

		ServiceResult<string> image = await _sandboxes.EnsureImageAsync(new SandboxImageRequest(context.ProjectKey, context.Dockerfile, Rebuild: false, WorkspaceLayout.ImageLogPath(context.ProjectFolder)), cancellationToken);
		if (!image.Succeeded || image.Value is null)
		{
			throw new SandboxException(image.Error ?? "The project's sandbox image could not be built.");
		}

		if (role == SandboxRole.Worker && work.Sequence == 1)
		{
			await RemoveAsync(context.ProjectFolder, context.Repository, work.CardId, cancellationToken);
		}

		string targetTip = await _git.RevParseAsync(hub, $"refs/heads/{target}", cancellationToken);
		string? cardTip = await _git.ResolveCommitAsync(hub, $"refs/heads/{branch}", cancellationToken);
		if (cardTip is null && role != SandboxRole.Worker)
		{
			throw new GitException($"The card's branch {branch} is not in the repository, so there is no work for this {work.Kind.DisplayName()} round. Edit the card so it starts again at work.");
		}

		string folder = WorkspaceLayout.SandboxFolder(context.ProjectFolder, work.CardId, SandboxNames.RoleName(role));
		string input = Path.Combine(folder, "in");
		string output = Path.Combine(folder, "out");
		EmptyFolder(input);
		EmptyFolder(output);

		SandboxSignIn signIn = role == SandboxRole.Gates || work.Agent is not AgentSnapshot agent
			? SandboxSignIn.None
			: _signIns.ForAgent(agent.Harness, Path.Combine(folder, SignInFolder));
		if (signIn.Problem is not null)
		{
			throw new SandboxException(signIn.Problem);
		}

		List<string> handIn = [$"refs/heads/{target}"];
		if (cardTip is not null)
		{
			handIn.Add($"refs/heads/{branch}");
		}

		await _git.CreateBundleAsync(hub, Path.Combine(input, HandInFile), handIn, cancellationToken);

		SandboxSpec spec = new(SandboxNames.Container(context.ProjectKey, work.CardId, role), image.Value, context.ProjectKey, work.CardId, role, input, output)
		{
			Volumes = Volumes(context.ProjectKey, work.CardId, role),
			Secrets = signIn.Secrets,
			Mounts = signIn.Mounts,
			Environment = signIn.Environment,
		};

		ISandbox sandbox = await _sandboxes.StartAsync(spec, cancellationToken);
		try
		{
			string repository = SandboxPaths.Combine(sandbox.WorkPath, RepositoryFolder);
			string baseCommit = cardTip ?? targetTip;
			await PrepareAsync(sandbox, repository, target, branch, baseCommit, keepLeftovers: role == SandboxRole.Worker, cancellationToken);

			if (role == SandboxRole.Review)
			{
				await TakeSteeringFilesFromTargetAsync(sandbox, repository, target, cancellationToken);
			}
			else if (role == SandboxRole.Resolve)
			{
				await StartMergeAsync(sandbox, repository, target, branch, targetTip, cancellationToken);
			}

			return new CardSandbox(sandbox, repository, branch, baseCommit, targetTip, cardTip is null, folder);
		}
		catch
		{
			await sandbox.DisposeAsync();

			throw;
		}
	}

	/// <summary>Commits whatever a worker or merge agent left, on the card's branch. A worker that switched branches has its work put back on the card's branch; Pyxie's own folder is never committed. A merge that is still in progress is completed with this message, whatever the files hold: conflict markers are caught when the resolution is taken back.</summary>
	/// <param name="card">The worker's sandbox.</param>
	/// <param name="message">The commit message.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="null"/> once committed, or nothing needed committing; otherwise why git refused, for the next attempt's briefing.</returns>
	public async Task<string?> CommitAsync(CardSandbox card, string message, CancellationToken cancellationToken)
	{
		ISandbox sandbox = card.Sandbox;
		string repository = card.RepositoryPath;

		ProcessResult current = await GitAsync(sandbox, repository, ["symbolic-ref", "--quiet", "HEAD"], cancellationToken);
		if (!current.Succeeded || current.StandardOutput.Trim() != $"refs/heads/{card.Branch}")
		{
			await GitCheckedAsync(sandbox, repository, ["checkout", "--quiet", "-B", card.Branch], cancellationToken);
		}

		// Pyxie's folder is ignored through info/exclude, which the worker could have changed, so it is also taken back out of what is staged.
		await GitCheckedAsync(sandbox, repository, ["add", "--all"], cancellationToken);
		await GitCheckedAsync(sandbox, repository, ["rm", "-r", "--cached", "--quiet", "--ignore-unmatch", "--", PipelineContract.WorkspaceFolder], cancellationToken);

		ProcessResult staged = await GitAsync(sandbox, repository, ["diff", "--cached", "--quiet"], cancellationToken);
		bool merging = (await GitAsync(sandbox, repository, ["rev-parse", "--quiet", "--verify", "MERGE_HEAD"], cancellationToken)).Succeeded;
		if (staged.Succeeded && !merging)
		{
			return null;
		}

		// Pyxie's identity goes in the environment, not in -c user.*: git takes an author or committer from the environment and from author.* and committer.* configuration, which the worker controls, before user.*.
		ProcessResult commit = await GitAsync(sandbox, repository, ["-c", "commit.gpgsign=false", "commit", "--quiet", "--message", message], cancellationToken, PyxieIdentity.Environment);

		return commit.Succeeded ? null : FirstLine(commit.Output);
	}

	/// <summary>Takes a worker's new commits back into the hub: bundled in the sandbox, copied out as a plain file, verified, fetched into a ref of Pyxie's own, checked to build on the commit the round started from, and only then made the card's branch, with a compare-and-swap.</summary>
	/// <param name="context">The open project.</param>
	/// <param name="card">The worker's sandbox, with its work committed.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="null"/> once the card's branch holds the work; otherwise why it could not be taken back.</returns>
	/// <exception cref="GitException">The hub could not take the bundle.</exception>
	public async Task<string?> TakeBackAsync(CardRoundContext context, CardSandbox card, CancellationToken cancellationToken)
	{
		string hub = context.Repository.Path;
		string head = (await GitCheckedAsync(card.Sandbox, card.RepositoryPath, ["rev-parse", "--verify", "HEAD"], cancellationToken)).Trim();

		if (head == card.BaseCommit)
		{
			return card.BranchIsNew ? await MoveBranchAsync(hub, card, card.BaseCommit, cancellationToken) : null;
		}

		return await FetchAndMoveAsync(hub, card, [card.BaseCommit], async tip => await _git.IsAncestorAsync(hub, card.BaseCommit, tip, cancellationToken) ? null : $"The work that came back does not build on {card.Branch}'s last commit, {Short(card.BaseCommit)}.", cancellationToken);
	}

	/// <summary>Takes a merge agent's resolution back into the hub the way <see cref="TakeBackAsync"/> takes work back, with the checks a resolution needs before the card's branch moves: the merge was completed, what came back is exactly one merge commit of the card's branch and the target branch commit the merge started from, and none of the files that conflicted still has conflict markers. A resolution that is not taken back changes nothing in the hub, and the next resolve round starts over in a fresh sandbox.</summary>
	/// <param name="context">The open project.</param>
	/// <param name="card">The merge agent's sandbox, with its merge committed (see <see cref="CommitAsync"/>).</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="null"/> once the card's branch is the merge commit; otherwise why the resolution was refused.</returns>
	/// <exception cref="GitException">The hub could not take the bundle.</exception>
	public async Task<string?> TakeBackResolutionAsync(CardRoundContext context, CardSandbox card, CancellationToken cancellationToken)
	{
		string hub = context.Repository.Path;
		string target = context.Repository.TargetBranch;
		string head = (await GitCheckedAsync(card.Sandbox, card.RepositoryPath, ["rev-parse", "--verify", "HEAD"], cancellationToken)).Trim();

		// An unchanged HEAD is success for a worker, who may have had nothing to do; here it means the merge was abandoned, and taking it as done would send the card around the conflict again and again.
		if (head == card.BaseCommit)
		{
			return $"The merge of {target} was not completed.";
		}

		// The hub already has both commits the merge joined, so the bundle carries only the merge commit and what the resolution added, never the target branch's history again.
		return await FetchAndMoveAsync(hub, card, [card.BaseCommit, card.TargetCommit], tip => CheckResolutionAsync(hub, target, card, tip, cancellationToken), cancellationToken);
	}

	/// <summary>Removes everything a card's rounds left: its sandboxes, their volumes and host folders, and its branch in the hub.</summary>
	/// <param name="projectFolder">The open project's folder.</param>
	/// <param name="repository">The project's repository.</param>
	/// <param name="cardId">The card.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when they are gone.</returns>
	public async Task RemoveAsync(string projectFolder, Repository repository, int cardId, CancellationToken cancellationToken)
	{
		await _sandboxes.RemoveCardAsync(SandboxNames.ProjectKey(projectFolder), cardId, cancellationToken);

		string branch = PipelineContract.BranchName(cardId);
		using (await _repositoryLock.AcquireAsync(cancellationToken))
		{
			if (await _git.BranchExistsAsync(repository.Path, branch, cancellationToken))
			{
				await _git.DeleteBranchAsync(repository.Path, branch, cancellationToken);
			}
		}

		Folders.Delete(WorkspaceLayout.CardSandboxesFolder(projectFolder, cardId));
	}

	/// <summary>Removes what cards that no longer need it left behind: cards that were deleted, merged, or closed while Pyxie was stopped, or before it could clean up. Gate and review sandboxes still there are removed too: none runs while the pipeline is starting.</summary>
	/// <param name="projectFolder">The open project's folder.</param>
	/// <param name="repository">The project's repository.</param>
	/// <param name="keepCardIds">The cards whose work is not finished.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The cards whose leftovers were removed.</returns>
	public async Task<IReadOnlyList<int>> RemoveAbandonedAsync(string projectFolder, Repository repository, IReadOnlySet<int> keepCardIds, CancellationToken cancellationToken)
	{
		string key = SandboxNames.ProjectKey(projectFolder);
		await _sandboxes.RemoveLeftoverRoundsAsync(key, cancellationToken);

		HashSet<int> cards = [.. await _sandboxes.ListCardsAsync(key, cancellationToken)];
		foreach (GitBranch branch in await _git.ListBranchesAsync(repository.Path, cancellationToken))
		{
			if (PipelineContract.TryParseBranchName(branch.Name, out int cardId))
			{
				cards.Add(cardId);
			}
		}

		string sandboxes = WorkspaceLayout.SandboxesFolder(projectFolder);
		if (Directory.Exists(sandboxes))
		{
			foreach (string folder in Directory.EnumerateDirectories(sandboxes))
			{
				string name = Path.GetFileName(folder);
				if (name.StartsWith("card-", StringComparison.Ordinal) && int.TryParse(name["card-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int cardId))
				{
					cards.Add(cardId);
				}
			}
		}

		List<int> removed = [];
		foreach (int cardId in cards.Where(card => !keepCardIds.Contains(card)).Order())
		{
			await RemoveAsync(projectFolder, repository, cardId, cancellationToken);
			removed.Add(cardId);
		}

		return removed;
	}

	private static IReadOnlyList<SandboxVolume> Volumes(string projectKey, int cardId, SandboxRole role)
	{
		return role switch
		{
			SandboxRole.Worker =>
			[
				new SandboxVolume(SandboxNames.Volume(projectKey, cardId, WorkVolume), "/work"),
				new SandboxVolume(SandboxNames.Volume(projectKey, cardId, HomeVolume), HomeInside),
			],

			// Gates build from a fresh clone each time; their home keeps package caches between the card's gate rounds.
			SandboxRole.Gates => [new SandboxVolume(SandboxNames.Volume(projectKey, cardId, GatesHomeVolume), HomeInside)],
			_ => [],
		};
	}

	private static void EmptyFolder(string folder)
	{
		Directory.CreateDirectory(folder);
		foreach (string file in Directory.EnumerateFiles(folder))
		{
			File.Delete(file);
		}

		foreach (string child in Directory.EnumerateDirectories(folder))
		{
			Folders.Delete(child);
		}
	}

	// The bundle was written by the sandbox, so it is checked to be a plain file of a sane size and copied into a folder only Pyxie writes before git reads it.
	private static void CopyBundle(string source, string destination)
	{
		FileInfo file = new(source);
		if (!file.Exists || file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
		{
			throw new SandboxException("The sandbox handed out something other than a bundle file.");
		}

		if (file.Length > MaxBundleBytes)
		{
			throw new SandboxException("The work handed out of the sandbox is larger than 2 GB.");
		}

		File.Copy(source, destination, overwrite: true);
	}

	private static int CardIdOf(string branch)
	{
		return PipelineContract.TryParseBranchName(branch, out int cardId) ? cardId : 0;
	}

	private static string FirstLine(string text)
	{
		string trimmed = text.Trim();
		int end = trimmed.IndexOf('\n', StringComparison.Ordinal);

		return end < 0 ? trimmed : trimmed[..end];
	}

	private static string Short(string commit)
	{
		return commit[..Math.Min(12, commit.Length)];
	}

	private static string DescribeParents(IReadOnlyList<string> parents)
	{
		return parents.Count switch
		{
			0 => "a commit with no parent",
			1 => $"a commit with one parent, {Short(parents[0])}",
			_ => $"a commit with {parents.Count} parents, {string.Join(", ", parents.Select(Short))}",
		};
	}

	private static Task<ProcessResult> GitAsync(ISandbox sandbox, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
	{
		SandboxCommand command = new("git", ["-c", "core.quotepath=off", "-c", "color.ui=never", .. arguments], workingDirectory) { Timeout = s_gitTimeout, MaxOutputLength = 1_000_000 };

		return sandbox.RunAsync(environment is null ? command : command with { Environment = environment }, cancellationToken);
	}

	private static async Task<string> GitCheckedAsync(ISandbox sandbox, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		ProcessResult result = await GitAsync(sandbox, workingDirectory, arguments, cancellationToken);

		return result.Succeeded ? result.StandardOutput : throw new SandboxException($"git {arguments[0]} failed in the sandbox: {FirstLine(result.Output)}");
	}

	// The card's branch is put where Pyxie says it is. A worker keeps what an earlier attempt left, commits included, as long as it builds on that commit; a merge left half-done is abandoned. Only the worker's sandbox outlives a round, so only it can hold locks a killed git command left, and a setup cut short.
	private static async Task PrepareAsync(ISandbox sandbox, string repository, string target, string branch, string baseCommit, bool keepLeftovers, CancellationToken cancellationToken)
	{
		if (!(await GitAsync(sandbox, sandbox.WorkPath, ["-C", repository, "rev-parse", "--git-dir"], cancellationToken)).Succeeded)
		{
			await GitCheckedAsync(sandbox, sandbox.WorkPath, ["init", "--quiet", "--initial-branch", branch, repository], cancellationToken);
		}

		// Nothing runs in the sandbox yet, so a lock found now was left by a git command that was killed.
		foreach (string lockFile in s_lockFiles.Concat([$"refs/heads/{branch}.lock", $"refs/heads/{target}.lock", $"refs/remotes/origin/{branch}.lock", $"refs/remotes/origin/{target}.lock"]))
		{
			await sandbox.DeleteFileAsync(SandboxPaths.Combine(repository, $".git/{lockFile}"), cancellationToken);
		}

		await GitCheckedAsync(sandbox, repository, ["config", "user.name", PyxieIdentity.Name], cancellationToken);
		await GitCheckedAsync(sandbox, repository, ["config", "user.email", PyxieIdentity.Email], cancellationToken);
		await sandbox.WriteFileAsync(SandboxPaths.Combine(repository, ".git/info/exclude"), $"/{PipelineContract.WorkspaceFolder}/\n", cancellationToken);

		string bundle = SandboxPaths.Combine(sandbox.InputPath, HandInFile);
		await GitCheckedAsync(sandbox, repository, ["fetch", "--quiet", "--force", "--no-tags", bundle, "+refs/heads/*:refs/remotes/origin/*"], cancellationToken);

		ProcessResult current = await GitAsync(sandbox, repository, ["symbolic-ref", "--quiet", "HEAD"], cancellationToken);
		bool onBranch = current.Succeeded && current.StandardOutput.Trim() == $"refs/heads/{branch}";
		bool buildsOnBase = onBranch && (await GitAsync(sandbox, repository, ["merge-base", "--is-ancestor", baseCommit, "HEAD"], cancellationToken)).Succeeded;

		if (keepLeftovers && buildsOnBase)
		{
			if ((await GitAsync(sandbox, repository, ["rev-parse", "--quiet", "--verify", "MERGE_HEAD"], cancellationToken)).Succeeded)
			{
				await GitAsync(sandbox, repository, ["merge", "--abort"], cancellationToken);
			}
		}
		else
		{
			await GitCheckedAsync(sandbox, repository, ["checkout", "--quiet", "--force", "-B", branch, baseCommit], cancellationToken);
			await GitCheckedAsync(sandbox, repository, ["clean", "-fd", "--quiet"], cancellationToken);
		}

		// A local target branch lets agents run git merge and git diff with its plain name.
		await GitCheckedAsync(sandbox, repository, ["branch", "--force", target, $"refs/remotes/origin/{target}"], cancellationToken);
	}

	private static async Task TakeSteeringFilesFromTargetAsync(ISandbox sandbox, string repository, string target, CancellationToken cancellationToken)
	{
		HashSet<string> onCard = [.. Paths(await GitCheckedAsync(sandbox, repository, ["ls-tree", "-r", "-z", "--name-only", "HEAD"], cancellationToken))];
		HashSet<string> onTarget = [.. Paths(await GitCheckedAsync(sandbox, repository, ["ls-tree", "-r", "-z", "--name-only", target], cancellationToken))];

		List<string> fromTarget = [.. onTarget.Where(SteeringFiles.Contains).Order(StringComparer.Ordinal)];
		if (fromTarget.Count > 0)
		{
			await GitCheckedAsync(sandbox, repository, ["checkout", "--quiet", target, "--", .. fromTarget], cancellationToken);
		}

		foreach (string path in onCard.Where(path => SteeringFiles.Contains(path) && !onTarget.Contains(path)))
		{
			await sandbox.DeleteFileAsync(SandboxPaths.Combine(repository, path), cancellationToken);
		}

		static IEnumerable<string> Paths(string output)
		{
			return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
		}
	}

	// Pyxie starts the merge so the agent only has to resolve it, and merges the commit it read when it opened the sandbox, which the hand-in bundle's target branch may have moved past. git reports conflicts with exit code 1, and also reports a merge it refused to start (a missing commit, say) with it, so only a merge in progress on that commit shows that the conflicts are the reason.
	private static async Task StartMergeAsync(ISandbox sandbox, string repository, string target, string branch, string targetCommit, CancellationToken cancellationToken)
	{
		ProcessResult merge = await GitAsync(sandbox, repository, ["merge", "--no-ff", "--no-commit", "--message", $"Merge {target} into {branch}", targetCommit], cancellationToken);
		ProcessResult merging = await GitAsync(sandbox, repository, ["rev-parse", "--quiet", "--verify", "MERGE_HEAD"], cancellationToken);

		if (merge is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 or 1 } || !merging.Succeeded || merging.StandardOutput.Trim() != targetCommit)
		{
			throw new SandboxException($"git merge {Short(targetCommit)} did not start a merge in the sandbox: {FirstLine(merge.Output)}");
		}
	}

	private async Task<string?> MoveBranchAsync(string hub, CardSandbox card, string tip, CancellationToken cancellationToken)
	{
		using (await _repositoryLock.AcquireAsync(cancellationToken))
		{
			bool moved = await _git.TryMoveBranchAsync(hub, card.Branch, tip, card.BranchIsNew ? string.Empty : card.BaseCommit, cancellationToken);

			return moved ? null : $"{card.Branch} moved in the repository while the round ran.";
		}
	}

	// The steps both take-backs share: the sandbox bundles what the round added to the commits the hub already has, the bundle is copied out as a plain file, verified, and fetched into a ref of Pyxie's own, and the check decides whether what came back may become the card's branch, which it does by compare-and-swap.
	private async Task<string?> FetchAndMoveAsync(string hub, CardSandbox card, IReadOnlyList<string> known, Func<string, Task<string?>> check, CancellationToken cancellationToken)
	{
		ProcessResult handOut = await GitAsync(card.Sandbox, card.RepositoryPath, ["bundle", "create", "--quiet", SandboxPaths.Combine(card.Sandbox.OutputPath, HandOutFile), $"refs/heads/{card.Branch}", .. known.Select(commit => $"^{commit}")], cancellationToken);
		if (!handOut.Succeeded)
		{
			return $"The work could not be handed out of the sandbox: {FirstLine(handOut.Output)}";
		}

		string received = Path.Combine(card.Folder, ReceivedFile);
		CopyBundle(Path.Combine(card.Sandbox.OutputFolder, HandOutFile), received);

		string incoming = string.Create(CultureInfo.InvariantCulture, $"refs/pyxie/incoming/card-{CardIdOf(card.Branch)}");
		try
		{
			await _git.VerifyBundleAsync(hub, received, cancellationToken);
			await _git.FetchBundleAsync(hub, received, $"+refs/heads/{card.Branch}:{incoming}", cancellationToken);
			string tip = await _git.RevParseAsync(hub, incoming, cancellationToken);

			if (await check(tip) is string problem)
			{
				return problem;
			}

			return await MoveBranchAsync(hub, card, tip, cancellationToken);
		}
		finally
		{
			File.Delete(received);
			await _git.DeleteRefAsync(hub, incoming, CancellationToken.None);
		}
	}

	// Everything is read from the hub, never from the sandbox, which the agent controlled.
	private async Task<string?> CheckResolutionAsync(string hub, string target, CardSandbox card, string tip, CancellationToken cancellationToken)
	{
		IReadOnlyList<string> parents = await _git.GetParentsAsync(hub, tip, cancellationToken);
		if (!parents.SequenceEqual([card.BaseCommit, card.TargetCommit]))
		{
			return $"The resolution must be one merge commit of {card.Branch} and {target}. What came back is {Short(tip)}, {DescribeParents(parents)}.";
		}

		IReadOnlyList<string> unresolved = await FindUnresolvedFilesAsync(hub, card, tip, cancellationToken);

		return unresolved.Count == 0 ? null : $"Conflict markers are left in: {string.Join(", ", unresolved)}.";
	}

	// The files that conflict are the ones git's own merge of the two parents could not do, which the hub works out for itself. A marker that one of the file's versions before the merge already had is not a leftover.
	private async Task<IReadOnlyList<string>> FindUnresolvedFilesAsync(string hub, CardSandbox card, string tip, CancellationToken cancellationToken)
	{
		GitMergeTree merge = await _git.MergeTreeAsync(hub, card.BaseCommit, card.TargetCommit, cancellationToken);

		List<string> unresolved = [];
		foreach (string file in merge.ConflictedFiles.Distinct().Order(StringComparer.Ordinal))
		{
			// A file the resolution deleted, and one that is binary or too large to search, has no lines to find a marker in.
			if (await ReadTextAsync(hub, tip, file, cancellationToken) is not string resolved)
			{
				continue;
			}

			string?[] versions = [await ReadTextAsync(hub, card.BaseCommit, file, cancellationToken), await ReadTextAsync(hub, card.TargetCommit, file, cancellationToken)];
			if (ConflictMarkers.HasUnresolved(resolved, versions))
			{
				unresolved.Add(file);
			}
		}

		return unresolved;
	}

	private async Task<string?> ReadTextAsync(string hub, string commit, string file, CancellationToken cancellationToken)
	{
		return (await _git.ReadFileAsync(hub, commit, file, MaxCheckedFileBytes, cancellationToken))?.Text;
	}
}
