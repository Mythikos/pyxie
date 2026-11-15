using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A merge commit is shown against its first parent, so it lists what it brought in from the other parent and not what the first parent already had.</summary>
public sealed class GitClientMergeCommitDetailTests
{
	private const int NoLimit = 1_000_000;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetCommitAsync_ForAMergeCommit_ShowsTheChangesComparedWithTheFirstParentOnly()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		await repository.RunGitAsync("merge", "--no-ff", "--no-edit", "feature");
		string mergeCommit = await repository.GetHashAsync();
		string againstFirstParent = await repository.RunGitAsync("diff", "--name-only", $"{mergeCommit}^1", mergeCommit);

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, mergeCommit, NoLimit, Token);

		Assert.Equal("feature-only.txt", againstFirstParent);
		Assert.NotNull(detail);
		Assert.Equal([divergence.MainTip, divergence.FeatureTip], detail.Commit.ParentShas);
		Assert.True(detail.Files.Select(file => file.Path).SequenceEqual(["feature-only.txt"]), $"A merge commit is compared with its first parent, so only feature-only.txt changed, but the files were: {string.Join(", ", detail.Files.Select(file => file.Path))}.");
		Assert.DoesNotContain("main-only.txt", detail.Patch, StringComparison.Ordinal);
	}
}
