using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>The git client parses what a command prints on standard output only, so a warning git prints on standard error never becomes part of a hash, a branch name, or a path.</summary>
public sealed class GitClientStandardOutputTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RevParseAsync_WhenGitWarnsOnStandardError_ReturnsOnlyTheHash()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		// A branch and a tag with the same name make git warn that the name is ambiguous, on standard error, while it still answers with the tag's commit.
		await repository.CreateBranchAsync("dup");
		string tagged = await repository.CommitFileAsync("tagged.txt", "after the branch was made");
		await repository.RunGitAsync("tag", "dup");

		ProcessResult raw = await repository.TryRunGitAsync("rev-parse", "--verify", "--end-of-options", "dup");
		string hash = await repository.Git.RevParseAsync(repository.Folder, "dup", Token);

		Assert.Contains("ambiguous", raw.Output, StringComparison.Ordinal);
		Assert.DoesNotContain("ambiguous", raw.StandardOutput, StringComparison.Ordinal);
		Assert.Equal(tagged, hash);
	}
}
