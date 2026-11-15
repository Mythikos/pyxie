using System.Globalization;
using System.Text;

using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>Adds many commits to a branch in one git process (<c>git fast-import</c>), for tests that need long histories without running git once per commit.</summary>
public static class EmptyCommits
{
	private static readonly DateTimeOffset s_firstCommitTime = new(2024, 3, 1, 12, 0, 0, TimeSpan.FromHours(2));
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Gets the author and committer time of the first commit <see cref="AddAsync"/> adds; each next commit is one minute later.</summary>
	public static DateTimeOffset FirstCommitTime => s_firstCommitTime;

	/// <summary>Gets the subject of the commit <see cref="AddAsync"/> adds at a position.</summary>
	/// <param name="position">The one-based position.</param>
	/// <returns>The subject, <c>Commit 1</c> for the first.</returns>
	public static string SubjectOf(int position)
	{
		return string.Create(CultureInfo.InvariantCulture, $"Commit {position}");
	}

	/// <summary>Appends commits that change nothing to a branch, each authored by <see cref="TempGitRepository.AuthorName"/>. The branch is created at the checked-out commit when it does not exist. A checked-out branch keeps its working tree, which the empty commits do not change.</summary>
	/// <param name="repository">The repository.</param>
	/// <param name="branch">The branch to extend.</param>
	/// <param name="count">How many commits to add.</param>
	/// <returns>A task that completes when the commits exist.</returns>
	public static async Task AddAsync(TempGitRepository repository, string branch, int count)
	{
		if (!await repository.Git.BranchExistsAsync(repository.Folder, branch, TestContext.Current.CancellationToken))
		{
			await repository.CreateBranchAsync(branch);
		}

		StringBuilder stream = new();
		for (int position = 1; position <= count; position++)
		{
			string message = SubjectOf(position) + "\n";
			long seconds = s_firstCommitTime.AddMinutes(position - 1).ToUnixTimeSeconds();

			stream.Append(CultureInfo.InvariantCulture, $"commit refs/heads/{branch}\n");
			stream.Append(CultureInfo.InvariantCulture, $"committer {TempGitRepository.AuthorName} <tests@pyxie.invalid> {seconds} +0200\n");
			stream.Append(CultureInfo.InvariantCulture, $"data {Encoding.UTF8.GetByteCount(message)}\n{message}");

			// Only the first commit names its parent; the next ones continue the branch.
			stream.Append(position == 1 ? $"from refs/heads/{branch}^0\n\n" : "\n");
		}

		ProcessRequest request = new("git", ["fast-import", "--quiet"], repository.Folder) { Timeout = s_commandTimeout, StandardInput = stream.ToString() };
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessResult result = await runner.RunAsync(request, TestContext.Current.CancellationToken);
		Assert.True(result.Succeeded, $"git fast-import failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");
	}
}
