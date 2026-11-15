using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>What git reports as changed in a working tree, for tests that only need to know a worktree is clean or which files are changed. It runs <c>git status --porcelain</c> itself, so it works on any folder (a repository's, a card's worktree, Pyxie's clone) without writing anything to it.</summary>
public static class WorkingTreeStatus
{
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Lists the changes git reports in a working tree, with every untracked file listed by its own path.</summary>
	/// <param name="folder">The working tree's folder.</param>
	/// <returns>One <c>git status --porcelain</c> line per change (<c> M file</c>, <c>A  file</c>, <c>?? file</c>), in git's order; empty when the working tree is clean. The test fails when git does not succeed.</returns>
	public static async Task<IReadOnlyList<string>> ListAsync(string folder)
	{
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = new("git", ["status", "--porcelain", "--untracked-files=all"], folder) { Timeout = s_commandTimeout };

		ProcessResult result = await runner.RunAsync(request, TestContext.Current.CancellationToken);

		Assert.True(result.Succeeded, $"git status failed in {folder} ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

		return result.StandardOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}
}
