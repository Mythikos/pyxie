using Pyxie.Runtime.Git;
using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>A real git repository in a temporary folder of its own: <c>main</c> is checked out and holds one commit, and a local identity is configured, so a test can import it into the runtime, branch it, and commit to it. Disposing it deletes the folder. <see cref="AttachAsync"/> gives the same helpers over a repository somebody else owns, such as Pyxie's hub, which is a bare repository: it reads and branches like any other, but has no working tree to write files in or check branches out of.</summary>
/// <remarks>
/// <para>
/// Every helper runs git through the runtime's <see cref="ProcessRunner"/> and <see cref="GitClient"/> and fails the test with git's output when a command does not succeed. Declare the repository before a <see cref="RuntimeHarness"/> that uses it (<c>await using</c> disposes in reverse order), so the harness stops the pipeline before the folder goes.
/// </para>
/// <para>
/// Importing the repository into the runtime clones it: the pipeline only ever writes to Pyxie's clone, so this folder is never changed and uncommitted changes here never reach the clone or block a merge. A test that wants to see what a card did, uses the hub (<c>RuntimeHarness.Clone</c>), which is an attached view of the same kind; to move the target branch on while the card runs, it clones the hub the way a person does (<c>ClonedRepository</c>), commits there, and pushes.
/// </para>
/// <para>The repository sets <c>core.autocrlf</c> to false and turns commit signing off locally, so file contents are exact and no machine-wide git setting changes what a test sees.</para>
/// </remarks>
public sealed class TempGitRepository : IAsyncDisposable
{
	/// <summary>The branch checked out in a new repository, and the target branch when the repository is connected.</summary>
	public const string MainBranch = "main";

	/// <summary>The file the initial commit adds.</summary>
	public const string InitialFile = "README.md";

	/// <summary>The content of <see cref="InitialFile"/>.</summary>
	public const string InitialContent = "initial";

	/// <summary>The commit identity configured in the repository.</summary>
	public const string AuthorName = "Pyxie Tests";

	private const int MaxReadBytes = 1_000_000;
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	private static readonly Dictionary<string, string> s_environment = new()
	{
		["GIT_TERMINAL_PROMPT"] = "0",
		["GIT_EDITOR"] = "true",
		["GIT_MERGE_AUTOEDIT"] = "no",
	};

	private readonly ProcessRunner _runner = new(TimeProvider.System);
	private readonly bool _ownsFolder;

	private TempGitRepository(string folder, bool ownsFolder, bool isBare)
	{
		Folder = folder;
		IsBare = isBare;
		GitFolder = isBare ? folder : Path.Combine(folder, ".git");
		_ownsFolder = ownsFolder;
		Git = new GitClient(_runner);
	}

	/// <summary>Gets the full path of the repository's working tree, which is also the folder to import into the runtime. For a bare repository it is the repository itself.</summary>
	public string Folder { get; }

	/// <summary>Gets a value indicating whether the repository is bare: git's files are in <see cref="Folder"/> itself, and there is no working tree.</summary>
	public bool IsBare { get; }

	/// <summary>Gets the folder that holds git's own files: <c>refs</c>, <c>hooks</c>, <c>info</c>, and <c>config</c>.</summary>
	public string GitFolder { get; }

	/// <summary>Gets the runtime's git client, for assertions that use the same git calls the runtime does.</summary>
	public GitClient Git { get; }

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Creates the repository: <c>git init -b main</c>, a local identity, and one initial commit that adds <see cref="InitialFile"/>.</summary>
	/// <returns>The repository, with <c>main</c> checked out and its working tree clean.</returns>
	public static async Task<TempGitRepository> CreateAsync()
	{
		TempGitRepository repository = new(TempFolder.Create("pyxie-repo"), ownsFolder: true, isBare: false);

		try
		{
			await repository.RunGitAsync("init", "--quiet", "-b", MainBranch);
			await repository.ConfigureAsync();
			await repository.WriteFileAsync(InitialFile, InitialContent);
			await repository.CommitAsync("Initial commit");

			return repository;
		}
		catch
		{
			await repository.DisposeAsync();

			throw;
		}
	}

	/// <summary>Wraps a repository that already exists and belongs to someone else, such as the clone Pyxie made, so a test can read and change it with the same helpers. The same local settings as <see cref="CreateAsync"/> are written to it, because a clone does not inherit them from its source and a machine-wide setting (signing, line-ending conversion, someone's identity) would otherwise decide what a test commits and reads. Disposing the result leaves the folder alone.</summary>
	/// <param name="folder">The working tree of the repository, or the repository itself when it is bare.</param>
	/// <returns>The repository, which the caller does not own.</returns>
	public static async Task<TempGitRepository> AttachAsync(string folder)
	{
		bool isBare;

		await using (TempGitRepository probe = new(folder, ownsFolder: false, isBare: false))
		{
			isBare = await probe.RunGitAsync("rev-parse", "--is-bare-repository") == "true";
		}

		TempGitRepository repository = new(folder, ownsFolder: false, isBare);
		await repository.ConfigureAsync();

		return repository;
	}

	/// <summary>Runs any git command in the working tree and returns its result without judging it, for commands a test expects to fail (a conflicting merge, say).</summary>
	/// <param name="arguments">The git arguments, each one argument: <c>["merge", "--no-ff", "feature"]</c>.</param>
	/// <returns>How the run ended, with the combined standard output and standard error.</returns>
	public async Task<ProcessResult> TryRunGitAsync(params string[] arguments)
	{
		ProcessRequest request = new("git", arguments, Folder) { Timeout = s_commandTimeout, Environment = s_environment };

		return await _runner.RunAsync(request, Token);
	}

	/// <summary>Runs any git command in the working tree and fails the test when it does not succeed.</summary>
	/// <param name="arguments">The git arguments, each one argument: <c>["log", "--oneline", "main"]</c>.</param>
	/// <returns>What git printed (standard output and standard error together), trimmed.</returns>
	public async Task<string> RunGitAsync(params string[] arguments)
	{
		ProcessResult result = await TryRunGitAsync(arguments);
		Assert.True(result.Succeeded, $"git {string.Join(' ', arguments)} failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

		return result.Output.Trim();
	}

	/// <summary>Writes a file in the working tree, creating its folders. Nothing is staged or committed.</summary>
	/// <param name="relativePath">The path from the repository root, with forward slashes.</param>
	/// <param name="content">The file's text, written as UTF-8 exactly as given.</param>
	/// <returns>A task that completes when the file is written.</returns>
	public async Task WriteFileAsync(string relativePath, string content)
	{
		RequireWorkingTree();
		string path = Path.GetFullPath(relativePath, Folder);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Folder);
		await File.WriteAllTextAsync(path, content, Token);
	}

	/// <summary>Commits everything changed in the working tree, new files included, on the checked-out branch.</summary>
	/// <param name="message">The commit message.</param>
	/// <returns>The new commit's hash. The test fails when there was nothing to commit.</returns>
	public async Task<string> CommitAsync(string message)
	{
		RequireWorkingTree();
		await RunGitAsync("add", "--all");
		ProcessResult staged = await TryRunGitAsync("diff", "--cached", "--quiet");
		Assert.False(staged.Succeeded, $"Nothing to commit for \"{message}\": the working tree had no changes.");
		await RunGitAsync("commit", "--quiet", "--message", message);

		return await GetHashAsync("HEAD");
	}

	/// <summary>Writes one file and commits it, on the checked-out branch.</summary>
	/// <param name="relativePath">The path from the repository root, with forward slashes.</param>
	/// <param name="content">The file's text.</param>
	/// <param name="message">The commit message; <c>Add</c> and the path unless given.</param>
	/// <returns>The new commit's hash.</returns>
	public async Task<string> CommitFileAsync(string relativePath, string content, string? message = null)
	{
		await WriteFileAsync(relativePath, content);

		return await CommitAsync(message ?? $"Add {relativePath}");
	}

	/// <summary>Creates a branch without checking it out.</summary>
	/// <param name="name">The branch name.</param>
	/// <param name="startPoint">Where the branch starts: a branch, tag, or commit; the checked-out commit unless given.</param>
	/// <returns>A task that completes when the branch exists.</returns>
	public async Task CreateBranchAsync(string name, string startPoint = "HEAD")
	{
		await RunGitAsync("branch", name, startPoint);
	}

	/// <summary>Checks a branch out in the working tree.</summary>
	/// <param name="branch">The branch name.</param>
	/// <returns>A task that completes when the branch is checked out.</returns>
	public async Task CheckoutAsync(string branch)
	{
		RequireWorkingTree();
		await RunGitAsync("checkout", "--quiet", branch);
	}

	/// <summary>Gets the branch checked out in the working tree.</summary>
	/// <returns>The branch name. The test fails when HEAD is detached.</returns>
	public async Task<string> GetCurrentBranchAsync()
	{
		string? branch = await Git.GetCurrentBranchAsync(Folder, Token);
		Assert.True(branch is not null, "No branch is checked out: HEAD is detached.");

		return branch;
	}

	/// <summary>Gets the hash a revision names.</summary>
	/// <param name="revision">A branch, tag, commit, or expression such as <c>main~1</c>; <c>HEAD</c> unless given.</param>
	/// <returns>The full commit hash. The test fails when the revision does not exist.</returns>
	public async Task<string> GetHashAsync(string revision = "HEAD")
	{
		return await Git.RevParseAsync(Folder, revision, Token);
	}

	/// <summary>Reads a file as it is committed at a revision, regardless of what the working tree holds. The text is read the way the runtime reads it (<see cref="GitClient.ReadFileAsync"/>), line by line from git's output, so every line break comes back as LF and a final line break is dropped: write test files without a trailing newline, or compare without it.</summary>
	/// <param name="revision">A branch, tag, or commit.</param>
	/// <param name="relativePath">The path from the repository root, with forward slashes.</param>
	/// <returns>The file's text, or <see langword="null"/> when the path is not a text file at that revision.</returns>
	public async Task<string?> ReadFileAtAsync(string revision, string relativePath)
	{
		GitFileContent? content = await Git.ReadFileAsync(Folder, revision, relativePath, MaxReadBytes, Token);

		return content?.Text;
	}

	/// <summary>Releases the process runner, and deletes the repository's folder, with the read-only files git leaves in it, unless the repository was attached with <see cref="AttachAsync"/>.</summary>
	/// <returns>A task that completes when the folder is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		_runner.Dispose();

		if (_ownsFolder)
		{
			await TempFolder.DeleteAsync(Folder);
		}
	}

	private void RequireWorkingTree()
	{
		if (IsBare)
		{
			throw new InvalidOperationException($"{Folder} is a bare repository and has no working tree: clone it, commit in the clone, and push.");
		}
	}

	private async Task ConfigureAsync()
	{
		await RunGitAsync("config", "user.name", AuthorName);
		await RunGitAsync("config", "user.email", "tests@pyxie.invalid");
		await RunGitAsync("config", "core.autocrlf", "false");
		await RunGitAsync("config", "commit.gpgsign", "false");
		await RunGitAsync("config", "tag.gpgsign", "false");
	}
}
