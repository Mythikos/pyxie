using System.Globalization;

using Pyxie.Runtime.Git;
using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>Commits and merges with a chosen author and committer date, and optionally a chosen identity, so a test that depends on how git orders, dates, or attributes commits does not depend on how fast it ran or on the repository's configured user. Each command runs through the runtime's process runner with the dates and the identity in that one process's environment, so nothing is shared between parallel tests and nothing leaks into the commits made without them.</summary>
public static class DatedCommits
{
	private const string DateFormat = "yyyy-MM-ddTHH:mm:sszzz";
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Gets the environment that makes a git command author and commit as someone, the way <see cref="PyxieIdentity.Environment"/> does for Pyxie.</summary>
	/// <param name="name">The name.</param>
	/// <param name="email">The email address.</param>
	/// <returns>The environment to pass as the identity of a commit or a merge.</returns>
	public static IReadOnlyDictionary<string, string> IdentityOf(string name, string email)
	{
		return new Dictionary<string, string>
		{
			["GIT_AUTHOR_NAME"] = name,
			["GIT_AUTHOR_EMAIL"] = email,
			["GIT_COMMITTER_NAME"] = name,
			["GIT_COMMITTER_EMAIL"] = email,
		};
	}

	/// <summary>Writes one file and commits everything changed on the checked-out branch, dated <paramref name="when"/>.</summary>
	/// <param name="repository">The repository, or the worktree folder to commit in.</param>
	/// <param name="relativePath">The file's path from the working tree's root.</param>
	/// <param name="content">The file's text.</param>
	/// <param name="when">The author and committer date. Give it a non-UTC offset to see the runtime convert it.</param>
	/// <param name="message">The commit message.</param>
	/// <param name="workingTree">The working tree to commit in; the repository's own folder unless given.</param>
	/// <param name="identity">The author and committer, as <see cref="IdentityOf"/> or <see cref="PyxieIdentity.Environment"/> give it; the repository's configured user unless given.</param>
	/// <returns>The new commit's hash.</returns>
	public static async Task<string> CommitFileAtAsync(TempGitRepository repository, string relativePath, string content, DateTimeOffset when, string message, string? workingTree = null, IReadOnlyDictionary<string, string>? identity = null)
	{
		string folder = workingTree ?? repository.Folder;
		string path = Path.GetFullPath(relativePath, folder);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? folder);
		await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
		await repository.RunGitAsync("-C", folder, "add", "--all");
		await RunAtAsync(folder, ["commit", "--quiet", "--message", message], when, identity);

		return await repository.RunGitAsync("-C", folder, "rev-parse", "HEAD");
	}

	/// <summary>Merges a branch into the checked-out branch with a merge commit, even when git could fast-forward, dated <paramref name="when"/>. The checked-out branch is the first parent and the merged branch the second.</summary>
	/// <param name="repository">The repository, or the worktree folder to merge in.</param>
	/// <param name="branch">The branch to merge in.</param>
	/// <param name="when">The author and committer date.</param>
	/// <param name="message">The merge commit's message.</param>
	/// <param name="workingTree">The working tree to merge in; the repository's own folder unless given.</param>
	/// <param name="identity">The author and committer, as <see cref="IdentityOf"/> or <see cref="PyxieIdentity.Environment"/> give it; the repository's configured user unless given.</param>
	/// <returns>The merge commit's hash.</returns>
	public static async Task<string> MergeAtAsync(TempGitRepository repository, string branch, DateTimeOffset when, string message, string? workingTree = null, IReadOnlyDictionary<string, string>? identity = null)
	{
		string folder = workingTree ?? repository.Folder;
		await RunAtAsync(folder, ["merge", "--quiet", "--no-ff", "--message", message, branch], when, identity);

		return await repository.RunGitAsync("-C", folder, "rev-parse", "HEAD");
	}

	private static async Task RunAtAsync(string folder, string[] arguments, DateTimeOffset when, IReadOnlyDictionary<string, string>? identity)
	{
		string date = when.ToString(DateFormat, CultureInfo.InvariantCulture);
		Dictionary<string, string> environment = new()
		{
			["GIT_TERMINAL_PROMPT"] = "0",
			["GIT_EDITOR"] = "true",
			["GIT_AUTHOR_DATE"] = date,
			["GIT_COMMITTER_DATE"] = date,
		};

		foreach ((string name, string value) in identity ?? new Dictionary<string, string>())
		{
			environment[name] = value;
		}

		ProcessRequest request = new("git", arguments, folder) { Timeout = s_commandTimeout, Environment = environment };
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessResult result = await runner.RunAsync(request, TestContext.Current.CancellationToken);
		Assert.True(result.Succeeded, $"git {arguments[0]} failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");
	}
}
