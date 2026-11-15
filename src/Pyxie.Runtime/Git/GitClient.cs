using System.Globalization;

using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Git;

/// <summary>Runs the git command line for Pyxie, always in a repository Pyxie trusts (the hub, or a source it imports): browsing it, moving branches, merging without a checkout, and moving commits in and out of sandboxes as bundles. It never runs in an agent's repository, whose own settings could make git run the agent's code. Every call names the directory it works in. Commands never prompt (credentials come from git's own credential helpers), and every commit Pyxie writes is made by <see cref="PyxieIdentity"/>, whatever identity the repository or the machine has configured.</summary>
public sealed class GitClient
{
	private const char FieldSeparator = '\0';
	private const char RecordSeparator = '\u001e';

	// The fields of a commit, in the order ParseCommit reads them. The last one is the card a Pyxie merge commit landed, from its trailer: git joins the values of several such trailers with a comma, so only a commit with exactly one is read as a card.
	private const string CommitFormat = "%H%x00%h%x00%P%x00%an%x00%ae%x00%aI%x00%s%x00%(trailers:key=" + PipelineContract.CardTrailerKey + ",valueonly,separator=%x2C)";
	private const int CommitFieldCount = 8;

	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(5);

	// A command that changes the repository is not cancelled part-way, so it gets longer to finish: a large checkout or a slow hook.
	private static readonly TimeSpan s_changeTimeout = TimeSpan.FromMinutes(30);
	private static readonly TimeSpan s_cloneTimeout = TimeSpan.FromMinutes(60);

	private static readonly Dictionary<string, string> s_environment = new()
	{
		["GIT_TERMINAL_PROMPT"] = "0",
		["GIT_OPTIONAL_LOCKS"] = "0",
		["GIT_EDITOR"] = "true",
		["GIT_MERGE_AUTOEDIT"] = "no",
	};

	private readonly ProcessRunner _runner;

	/// <summary>Initializes a new instance of the <see cref="GitClient"/> class.</summary>
	/// <param name="runner">The runner that starts git.</param>
	public GitClient(ProcessRunner runner)
	{
		_runner = runner;
	}

	/// <summary>Finds the top of the working tree that contains a path.</summary>
	/// <param name="path">A directory inside a working tree.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The full path of the working tree's top directory, or <see langword="null"/> when the path is not inside one.</returns>
	public async Task<string?> FindTopLevelAsync(string path, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(path, ["rev-parse", "--show-toplevel"], cancellationToken);

		return result.Succeeded && result.StandardOutput.Trim() is { Length: > 0 } top ? Path.GetFullPath(top) : null;
	}

	/// <summary>Gets the branch checked out in a working tree.</summary>
	/// <param name="workingTree">The working tree.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The branch name, or <see langword="null"/> when HEAD is detached.</returns>
	public async Task<string?> GetCurrentBranchAsync(string workingTree, CancellationToken cancellationToken)
	{
		// The full ref, not --short: git shortens a branch that shares its name with a tag to heads/<name>.
		ProcessResult result = await RunAsync(workingTree, ["symbolic-ref", "--quiet", "HEAD"], cancellationToken, workingTreeOnly: true);
		const string heads = "refs/heads/";

		return result.Succeeded && result.StandardOutput.Trim() is { Length: > 0 } reference && reference.StartsWith(heads, StringComparison.Ordinal) ? reference[heads.Length..] : null;
	}

	/// <summary>Determines whether a local branch exists.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="branch">The branch name.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="true"/> when <c>refs/heads/</c><paramref name="branch"/> exists.</returns>
	public async Task<bool> BranchExistsAsync(string directory, string branch, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"], cancellationToken);

		return result.Succeeded;
	}

	/// <summary>Determines whether a name is a valid branch name.</summary>
	/// <param name="directory">A directory to run git in.</param>
	/// <param name="name">The candidate name.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="true"/> when git accepts it as a branch name.</returns>
	public async Task<bool> IsValidBranchNameAsync(string directory, string name, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["check-ref-format", "--branch", name], cancellationToken);

		return result.Succeeded;
	}

	/// <summary>Lists the local branches, most recently committed first.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The branches.</returns>
	public async Task<IReadOnlyList<GitBranch>> ListBranchesAsync(string directory, CancellationToken cancellationToken)
	{
		string output = await RunCheckedAsync(directory, ["for-each-ref", "--sort=-committerdate", "--format=%(refname:lstrip=2)%00%(objectname)%00%(committerdate:iso-strict)%00%(subject)%1e", "refs/heads"], cancellationToken);

		List<GitBranch> branches = [];
		foreach (string record in Records(output))
		{
			string[] fields = record.Split(FieldSeparator);
			if (fields.Length >= 4)
			{
				branches.Add(new GitBranch(fields[0], fields[1], ParseUtc(fields[2]), fields[3]));
			}
		}

		return branches;
	}

	/// <summary>Resolves a revision to a commit.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">A branch, tag, or hash.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The full commit hash, or <see langword="null"/> when the revision does not name a commit.</returns>
	public async Task<string?> ResolveCommitAsync(string directory, string revision, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"], cancellationToken);

		return result.Succeeded && result.StandardOutput.Trim() is { Length: > 0 } sha ? sha : null;
	}

	/// <summary>Lists a directory at a revision: directories first, then files, each by name.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The commit to look at.</param>
	/// <param name="path">The directory's path from the repository root, with forward slashes; empty for the root.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The entries, or <see langword="null"/> when the path is not a directory at that revision.</returns>
	public async Task<IReadOnlyList<GitTreeEntry>?> ListTreeAsync(string directory, string revision, string path, CancellationToken cancellationToken)
	{
		string normalized = path.Trim('/');
		string treeish = normalized.Length == 0 ? $"{revision}^{{tree}}" : $"{revision}:{normalized}";
		ProcessResult result = await RunAsync(directory, ["ls-tree", "-l", "-z", "--end-of-options", treeish], cancellationToken);
		if (!result.Succeeded)
		{
			return null;
		}

		List<GitTreeEntry> entries = [];
		foreach (string record in result.StandardOutput.Split(FieldSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			int tab = record.IndexOf('\t', StringComparison.Ordinal);
			if (tab < 0)
			{
				continue;
			}

			string[] fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string name = record[(tab + 1)..];
			if (fields.Length < 4)
			{
				continue;
			}

			GitTreeEntryKind kind = fields[1] switch
			{
				"tree" => GitTreeEntryKind.Directory,
				"commit" => GitTreeEntryKind.Submodule,
				_ => GitTreeEntryKind.File,
			};

			long? size = kind == GitTreeEntryKind.File && long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) ? bytes : null;
			entries.Add(new GitTreeEntry(name, normalized.Length == 0 ? name : $"{normalized}/{name}", kind, size));
		}

		return [.. entries.OrderBy(entry => entry.Kind == GitTreeEntryKind.Directory ? 0 : 1).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>Reads a file at a revision.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The commit to look at.</param>
	/// <param name="path">The file's path from the repository root, with forward slashes.</param>
	/// <param name="maxBytes">The largest file whose text is read.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The content, or <see langword="null"/> when the path is not a file at that revision.</returns>
	public async Task<GitFileContent?> ReadFileAsync(string directory, string revision, string path, int maxBytes, CancellationToken cancellationToken)
	{
		string normalized = path.Trim('/');
		string objectName = $"{revision}:{normalized}";

		ProcessResult type = await RunAsync(directory, ["cat-file", "-t", "--end-of-options", objectName], cancellationToken);
		if (!type.Succeeded || type.StandardOutput.Trim() != "blob")
		{
			return null;
		}

		string sizeText = await RunCheckedAsync(directory, ["cat-file", "-s", "--end-of-options", objectName], cancellationToken);
		long size = long.Parse(sizeText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
		if (size > maxBytes)
		{
			return new GitFileContent(normalized, size, IsBinary: false, IsTooLarge: true, Text: null);
		}

		ProcessResult content = await RunAsync(directory, ["cat-file", "blob", "--end-of-options", objectName], cancellationToken, maxOutputLength: maxBytes + 1024);
		if (!content.Succeeded)
		{
			throw Failure("cat-file", content);
		}

		bool binary = content.StandardOutput.Contains('\0', StringComparison.Ordinal);

		return new GitFileContent(normalized, size, binary, IsTooLarge: false, binary ? null : content.StandardOutput);
	}

	/// <summary>Lists commits reachable from a revision, newest first.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The commit to start from.</param>
	/// <param name="skip">How many commits to skip, for paging.</param>
	/// <param name="take">How many commits to return.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The commits; empty when the revision does not exist.</returns>
	public async Task<IReadOnlyList<GitCommit>> LogAsync(string directory, string revision, int skip, int take, CancellationToken cancellationToken)
	{
		return await ReadLogAsync(directory, revision, skip, take, firstParentOnly: false, cancellationToken);
	}

	/// <summary>Lists the commits of a range along the first parent only, newest first: for a target branch, what landed on it, each merge as one entry instead of every commit the merge brought in.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="range">A revision range, such as <c>cardTip..targetTip</c>: the commits reachable from the second that are not reachable from the first.</param>
	/// <param name="take">How many commits to return.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The commits; empty when the range does not exist or holds none.</returns>
	public async Task<IReadOnlyList<GitCommit>> LogFirstParentAsync(string directory, string range, int take, CancellationToken cancellationToken)
	{
		return await ReadLogAsync(directory, range, skip: 0, take, firstParentOnly: true, cancellationToken);
	}

	/// <summary>Gets a commit's parents.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The commit.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The full hashes of its parents, in order: none for a root commit, two or more for a merge.</returns>
	/// <exception cref="GitException">The revision does not name a commit.</exception>
	public async Task<IReadOnlyList<string>> GetParentsAsync(string directory, string revision, CancellationToken cancellationToken)
	{
		string output = await RunCheckedAsync(directory, ["log", "--max-count=1", "--format=%P", "--end-of-options", $"{revision}^{{commit}}"], cancellationToken);

		return [.. output.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)];
	}

	/// <summary>Shows how a merge commit resolved its conflicts: <c>git show --remerge-diff</c>, which merges the commit's parents again, with the conflict markers a person would have seen, and diffs that against the commit. A merge that was clean shows nothing, and a commit that is not a merge shows its own changes. git writes the merged trees to a temporary object directory of the repository while it runs and removes it again.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="commit">The merge commit.</param>
	/// <param name="maxLength">The most characters of diff to return; a longer diff keeps its beginning and its end with a marker for what was cut.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The diff; empty when the merge was clean.</returns>
	/// <exception cref="GitException">git could not show the commit.</exception>
	public async Task<string> RemergeDiffAsync(string directory, string commit, int maxLength, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["show", "--remerge-diff", "--format=", "--no-color", "--end-of-options", commit], cancellationToken, maxOutputLength: maxLength);
		if (!result.Succeeded)
		{
			throw Failure("show", result);
		}

		return result.StandardOutput.Trim('\n', '\r');
	}

	/// <summary>Gets a commit with its full message, the files it changed, and its diff, compared with its first parent.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The commit.</param>
	/// <param name="maxPatchLength">The most characters of diff to return.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The commit, or <see langword="null"/> when it does not exist.</returns>
	public async Task<GitCommitDetail?> GetCommitAsync(string directory, string revision, int maxPatchLength, CancellationToken cancellationToken)
	{
		ProcessResult meta = await RunAsync(directory, ["show", "-s", $"--format={CommitFormat}%x00%B", "--end-of-options", $"{revision}^{{commit}}"], cancellationToken);
		if (!meta.Succeeded)
		{
			return null;
		}

		string[] fields = meta.StandardOutput.Split(FieldSeparator);
		if (ParseCommit(fields) is not GitCommit commit)
		{
			return null;
		}

		string message = fields.Length > CommitFieldCount ? fields[CommitFieldCount].Trim() : commit.Subject;
		// Compared with the first parent explicitly: diff-tree ignores --first-parent, so a merge would otherwise also show what the other parent brought in.
		string[] range = commit.ParentShas.Count > 0 ? [commit.ParentShas[0], commit.Sha] : ["--root", commit.Sha];
		string numstat = await RunCheckedAsync(directory, ["diff-tree", "-r", "-M", "--no-commit-id", "--numstat", "-z", .. range], cancellationToken);
		ProcessResult patch = await RunAsync(directory, ["diff-tree", "-r", "-M", "--no-commit-id", "-p", .. range], cancellationToken, maxOutputLength: maxPatchLength);

		return new GitCommitDetail(commit, message, ParseNumstat(numstat), patch.StandardOutput);
	}

	/// <summary>Gets the repository's common git directory, shared by all its worktrees.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The full path.</returns>
	public async Task<string> GetCommonDirectoryAsync(string directory, CancellationToken cancellationToken)
	{
		string output = await RunCheckedAsync(directory, ["rev-parse", "--path-format=absolute", "--git-common-dir"], cancellationToken);

		return Path.GetFullPath(output.Trim());
	}

	/// <summary>Deletes a local branch whether or not it was merged.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="branch">The branch.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the branch is gone.</returns>
	public async Task DeleteBranchAsync(string directory, string branch, CancellationToken cancellationToken)
	{
		await RunCheckedAsync(directory, ["branch", "-D", branch], cancellationToken, GitAccess.Change);
	}

	/// <summary>Resolves a revision that must exist.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The revision.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The full object hash.</returns>
	public async Task<string> RevParseAsync(string directory, string revision, CancellationToken cancellationToken)
	{
		string output = await RunCheckedAsync(directory, ["rev-parse", "--verify", "--end-of-options", revision], cancellationToken);

		return output.Trim();
	}

	/// <summary>Determines whether one commit is an ancestor of (or the same as) another.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="ancestor">The possible ancestor.</param>
	/// <param name="descendant">The possible descendant.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="true"/> when <paramref name="descendant"/> contains <paramref name="ancestor"/>.</returns>
	public async Task<bool> IsAncestorAsync(string directory, string ancestor, string descendant, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["merge-base", "--is-ancestor", ancestor, descendant], cancellationToken);

		return result switch
		{
			{ Outcome: ProcessOutcome.Exited, ExitCode: 0 } => true,
			{ Outcome: ProcessOutcome.Exited, ExitCode: 1 } => false,
			_ => throw Failure("merge-base", result),
		};
	}

	/// <summary>Creates a commit object from a tree and parents without touching any branch or working tree. The commit is authored and committed by <see cref="PyxieIdentity"/>, whatever identity the repository or the machine has configured, because the commit is Pyxie's own.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="tree">The tree.</param>
	/// <param name="parents">The parent commits, in order.</param>
	/// <param name="message">The commit message. A git trailer, such as <c>Pyxie-Card: 3</c>, must be the last paragraph to be read as one.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The new commit's hash.</returns>
	public async Task<string> CommitTreeAsync(string directory, string tree, IReadOnlyList<string> parents, string message, CancellationToken cancellationToken)
	{
		List<string> arguments = ["commit-tree", tree];
		foreach (string parent in parents)
		{
			arguments.Add("-p");
			arguments.Add(parent);
		}

		arguments.Add("-m");
		arguments.Add(message);

		string output = await RunCheckedAsync(directory, arguments, cancellationToken, GitAccess.Change, environment: PyxieIdentity.Environment);

		return output.Trim();
	}

	/// <summary>Merges two commits without a working tree, the way <c>git merge</c> would, and writes the merged tree.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="ours">The commit merged into.</param>
	/// <param name="theirs">The commit merged in.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The merged tree, and the files that conflict; with conflicts, the tree holds conflict markers and must not be committed.</returns>
	/// <exception cref="GitException">git could not merge them for another reason.</exception>
	public async Task<GitMergeTree> MergeTreeAsync(string directory, string ours, string theirs, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["merge-tree", "--write-tree", "--name-only", "--no-messages", "-z", ours, theirs], cancellationToken, GitAccess.Change);
		if (result.Outcome != ProcessOutcome.Exited || result.ExitCode is not (0 or 1))
		{
			throw Failure("merge-tree", result);
		}

		string[] fields = result.StandardOutput.Split(FieldSeparator, StringSplitOptions.RemoveEmptyEntries);
		if (fields.Length == 0)
		{
			throw Failure("merge-tree", result);
		}

		return new GitMergeTree(fields[0].Trim(), result.ExitCode == 0 ? [] : [.. fields.Skip(1).Select(field => field.Trim('\n')).Where(field => field.Length > 0)]);
	}

	/// <summary>Writes a bundle: a file that carries commits from one repository to another without either running anything of the other's.</summary>
	/// <param name="directory">A directory of the repository the commits are in.</param>
	/// <param name="bundlePath">The bundle file to write.</param>
	/// <param name="revisions">What goes in: branches as full ref names, and <c>^commit</c> for commits the receiver already has.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the bundle is written.</returns>
	public async Task CreateBundleAsync(string directory, string bundlePath, IReadOnlyList<string> revisions, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(bundlePath) ?? directory);
		await RunCheckedAsync(directory, ["bundle", "create", "--quiet", bundlePath, .. revisions], cancellationToken, GitAccess.Change);
	}

	/// <summary>Checks that a bundle is a bundle and that the repository has every commit it builds on.</summary>
	/// <param name="directory">A directory of the receiving repository.</param>
	/// <param name="bundlePath">The bundle file.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the bundle is found sound.</returns>
	/// <exception cref="GitException">The bundle is not sound.</exception>
	public async Task VerifyBundleAsync(string directory, string bundlePath, CancellationToken cancellationToken)
	{
		// Without --quiet git names the commits the repository lacks; on success it lists the bundle's refs, which nobody reads.
		await RunCheckedAsync(directory, ["bundle", "verify", bundlePath], cancellationToken);
	}

	/// <summary>
	/// Fetches one branch from a bundle into a ref the caller chooses, checking every object it brings. Nothing the bundle names other than the source of <paramref name="refspec"/> is taken.
	/// </summary>
	/// <param name="directory">A directory of the receiving repository.</param>
	/// <param name="bundlePath">The bundle file.</param>
	/// <param name="refspec">An explicit refspec, such as <c>+refs/heads/pyxie/card-1:refs/pyxie/incoming/card-1</c>.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the ref is written.</returns>
	public async Task FetchBundleAsync(string directory, string bundlePath, string refspec, CancellationToken cancellationToken)
	{
		await RunCheckedAsync(directory, ["-c", "fetch.fsckObjects=true", "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", "--no-recurse-submodules", bundlePath, refspec], cancellationToken, GitAccess.Change);
	}

	/// <summary>Deletes a ref, if it is there.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="name">The full ref name.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the ref is gone.</returns>
	public async Task DeleteRefAsync(string directory, string name, CancellationToken cancellationToken)
	{
		await RunCheckedAsync(directory, ["update-ref", "-d", name], cancellationToken, GitAccess.Change);
	}

	/// <summary>Moves a branch to a commit only if it still points where the caller last saw it.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="branch">The branch.</param>
	/// <param name="newCommit">The commit to move it to.</param>
	/// <param name="expectedCommit">The commit it must point at now.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns><see langword="true"/> when the branch moved; <see langword="false"/> when it had moved meanwhile.</returns>
	/// <exception cref="GitException">git could not move the branch for another reason, such as a lock file or a permission error.</exception>
	public async Task<bool> TryMoveBranchAsync(string directory, string branch, string newCommit, string expectedCommit, CancellationToken cancellationToken)
	{
		ProcessResult result = await RunAsync(directory, ["update-ref", "-m", "pyxie: merge", $"refs/heads/{branch}", newCommit, expectedCommit], cancellationToken, GitAccess.Change);
		if (result.Succeeded)
		{
			return true;
		}

		// update-ref fails the same way for a lost compare-and-swap and for everything else, so the branch is read back to tell them apart.
		ProcessResult current = await RunAsync(directory, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"], CancellationToken.None);
		if (current.Succeeded && current.StandardOutput.Trim() != expectedCommit)
		{
			return false;
		}

		throw Failure("update-ref", result);
	}

	/// <summary>Clones a repository as a bare repository: it has no files checked out, HEAD names the source's checked-out branch, and every branch of the source is a branch of the clone. The clone shares no files with a local source.</summary>
	/// <param name="url">The URL or path to clone.</param>
	/// <param name="destination">The folder to clone into; it must not exist.</param>
	/// <param name="cancellationToken">A token to cancel the clone.</param>
	/// <returns>git's result: a failure says why in its output.</returns>
	public async Task<ProcessResult> CloneBareAsync(string url, string destination, CancellationToken cancellationToken)
	{
		string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException($"The clone destination {destination} has no parent folder.", nameof(destination));
		Directory.CreateDirectory(parent);

		// A local clone would otherwise hardlink the source's object files, so anything done to the clone's files (clearing read-only flags to delete it, say) would reach the source.
		return await RunAsync(parent, ["clone", "--bare", "--no-hardlinks", "--", url, destination], cancellationToken, timeout: s_cloneTimeout);
	}

	/// <summary>Sets up a fresh bare clone as a hub that people push to and pull from. The clone's remote for the source is removed, so the hub keeps no way back to it: only Pyxie decides what goes to or comes from the source. Its <see cref="HubHooks"/> leave people's own branches to them, keep the target branch from being rewritten or deleted, because card branches are built on it, and keep pushes off the pipeline's <c>pyxie/</c> branches.</summary>
	/// <param name="repository">The bare clone.</param>
	/// <param name="branch">The target branch.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the hub is set up.</returns>
	public async Task SetUpHubAsync(string repository, string branch, CancellationToken cancellationToken)
	{
		await RunCheckedAsync(repository, ["remote", "remove", "origin"], cancellationToken, GitAccess.Change);
		await RunCheckedAsync(repository, ["config", HubHooks.TargetBranchKey, branch], cancellationToken, GitAccess.Change);

		// A hooks folder set in the person's global git config would replace the hub's own, and the hub's protection with it; the hub's own setting wins over a global one.
		string hooks = Path.Combine(await GetCommonDirectoryAsync(repository, cancellationToken), "hooks");
		await RunCheckedAsync(repository, ["config", "core.hooksPath", hooks], cancellationToken, GitAccess.Change);
		await HubHooks.WriteAsync(hooks, cancellationToken);
	}

	/// <summary>Writes the files of a commit to a zip file, inside one folder.</summary>
	/// <param name="directory">A directory of the repository.</param>
	/// <param name="revision">The commit, branch, or tag.</param>
	/// <param name="folder">The folder the files are put in inside the zip.</param>
	/// <param name="zipPath">The zip file to write; it is replaced if it exists.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the zip file is written.</returns>
	public async Task ArchiveAsync(string directory, string revision, string folder, string zipPath, CancellationToken cancellationToken)
	{
		await RunCheckedAsync(directory, ["archive", "--format=zip", $"--prefix={folder}/", "-o", zipPath, "--end-of-options", revision], cancellationToken);
	}

	private async Task<IReadOnlyList<GitCommit>> ReadLogAsync(string directory, string revision, int skip, int take, bool firstParentOnly, CancellationToken cancellationToken)
	{
		List<string> arguments = ["log", $"--format={CommitFormat}%x1e", $"--skip={skip}", $"--max-count={take}"];
		if (firstParentOnly)
		{
			arguments.Add("--first-parent");
		}

		arguments.Add("--end-of-options");
		arguments.Add(revision);

		ProcessResult result = await RunAsync(directory, arguments, cancellationToken);
		if (!result.Succeeded)
		{
			return [];
		}

		List<GitCommit> commits = [];
		foreach (string record in Records(result.StandardOutput))
		{
			if (ParseCommit(record.Split(FieldSeparator)) is GitCommit commit)
			{
				commits.Add(commit);
			}
		}

		return commits;
	}

	private static IEnumerable<string> Records(string output)
	{
		return output.Split(RecordSeparator).Select(record => record.Trim('\n', '\r')).Where(record => record.Length > 0);
	}

	private static GitCommit? ParseCommit(string[] fields)
	{
		if (fields.Length < CommitFieldCount)
		{
			return null;
		}

		string[] parents = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);

		return new GitCommit(fields[0].Trim(), fields[1], parents, fields[3], fields[4], ParseUtc(fields[5]), fields[6].TrimEnd('\n'), ParseCardId(fields[7]));
	}

	// A commit that landed one card has exactly one card trailer: a whole number of at least 1. None, several (git joins them with a comma), or anything else is not a landed card.
	private static int? ParseCardId(string trailerValues)
	{
		return int.TryParse(trailerValues.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int cardId) && cardId > 0 ? cardId : null;
	}

	private static DateTime ParseUtc(string value)
	{
		return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) ? parsed.UtcDateTime : DateTime.MinValue;
	}

	// With -z, an entry is "added<TAB>deleted<TAB>path<NUL>", or for a rename "added<TAB>deleted<TAB><NUL>old<NUL>new<NUL>"; binary files count "-".
	private static List<GitChangedFile> ParseNumstat(string output)
	{
		List<GitChangedFile> files = [];
		string[] parts = output.Split(FieldSeparator);

		for (int index = 0; index < parts.Length; index++)
		{
			string[] stats = parts[index].TrimStart('\n').Split('\t');
			if (stats.Length < 3)
			{
				continue;
			}

			int? additions = int.TryParse(stats[0], NumberStyles.None, CultureInfo.InvariantCulture, out int added) ? added : null;
			int? deletions = int.TryParse(stats[1], NumberStyles.None, CultureInfo.InvariantCulture, out int deleted) ? deleted : null;

			if (stats[2].Length > 0)
			{
				files.Add(new GitChangedFile(stats[2], null, additions, deletions));
			}
			else if (index + 2 < parts.Length)
			{
				files.Add(new GitChangedFile(parts[index + 2], parts[index + 1], additions, deletions));
				index += 2;
			}
		}

		return files;
	}

	private static GitException Failure(string command, ProcessResult result)
	{
		string reason = result.Outcome switch
		{
			ProcessOutcome.NotStarted => result.StartError ?? "git could not start.",
			ProcessOutcome.TimedOut => "it timed out.",
			_ => $"exit code {result.ExitCode}.",
		};

		string output = result.Output.Trim();

		return new GitException(output.Length == 0 ? $"git {command} failed: {reason}" : $"git {command} failed: {reason}\n{output}");
	}

	private async Task<string> RunCheckedAsync(
		string directory,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken,
		GitAccess access = GitAccess.Read,
		bool workingTreeOnly = false,
		IReadOnlyDictionary<string, string>? environment = null)
	{
		ProcessResult result = await RunAsync(directory, arguments, cancellationToken, access, workingTreeOnly, environment: environment);

		return result.Succeeded ? result.StandardOutput : throw Failure(arguments.FirstOrDefault(argument => !argument.StartsWith('-') && !argument.Contains('=', StringComparison.Ordinal)) ?? "command", result);
	}

	/// <summary>
	/// Runs git. A command that changes the repository is not cancelled once started (see <see cref="GitAccess.Change"/>). A command on a working tree (<paramref name="workingTreeOnly"/>) never looks above it for a repository, so a working tree that lost its <c>.git</c> fails instead of reaching a repository around it.
	/// </summary>
	private async Task<ProcessResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken, GitAccess access = GitAccess.Read, bool workingTreeOnly = false, int maxOutputLength = 1_000_000, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environment = null)
	{
		cancellationToken.ThrowIfCancellationRequested();

		Dictionary<string, string> variables = new(s_environment);
		if (workingTreeOnly && Path.GetDirectoryName(Path.GetFullPath(directory)) is string parent)
		{
			variables["GIT_CEILING_DIRECTORIES"] = parent;
		}

		if (environment is not null)
		{
			foreach ((string name, string value) in environment)
			{
				variables[name] = value;
			}
		}

		ProcessRequest request = new("git", ["-c", "core.quotepath=off", "-c", "color.ui=never", .. arguments], directory)
		{
			Timeout = timeout ?? (access == GitAccess.Change ? s_changeTimeout : s_commandTimeout),
			MaxOutputLength = maxOutputLength,
			Environment = variables,
		};

		return await _runner.RunAsync(request, access == GitAccess.Change ? CancellationToken.None : cancellationToken);
	}
}
