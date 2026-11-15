using System.Globalization;

using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>Commits with a chosen author and committer date, so a test that depends on how git orders or dates commits does not depend on how fast it ran. The commit runs through the runtime's process runner with the dates in that one process's environment, so nothing is shared between parallel tests.</summary>
public static class DatedCommits
{
	private const string DateFormat = "yyyy-MM-ddTHH:mm:sszzz";
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Writes one file and commits everything changed on the checked-out branch, dated <paramref name="when"/>.</summary>
	/// <param name="repository">The repository, or the worktree folder to commit in.</param>
	/// <param name="relativePath">The file's path from the working tree's root.</param>
	/// <param name="content">The file's text.</param>
	/// <param name="when">The author and committer date. Give it a non-UTC offset to see the runtime convert it.</param>
	/// <param name="message">The commit message.</param>
	/// <param name="workingTree">The working tree to commit in; the repository's own folder unless given.</param>
	/// <returns>The new commit's hash.</returns>
	public static async Task<string> CommitFileAtAsync(TempGitRepository repository, string relativePath, string content, DateTimeOffset when, string message, string? workingTree = null)
	{
		string folder = workingTree ?? repository.Folder;
		string path = Path.GetFullPath(relativePath, folder);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? folder);
		await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
		await repository.RunGitAsync("-C", folder, "add", "--all");

		string date = when.ToString(DateFormat, CultureInfo.InvariantCulture);
		Dictionary<string, string> environment = new()
		{
			["GIT_TERMINAL_PROMPT"] = "0",
			["GIT_EDITOR"] = "true",
			["GIT_AUTHOR_DATE"] = date,
			["GIT_COMMITTER_DATE"] = date,
		};

		ProcessRequest request = new("git", ["commit", "--quiet", "--message", message], folder) { Timeout = s_commandTimeout, Environment = environment };
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessResult result = await runner.RunAsync(request, TestContext.Current.CancellationToken);
		Assert.True(result.Succeeded, $"git commit failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

		return await repository.RunGitAsync("-C", folder, "rev-parse", "HEAD");
	}
}
