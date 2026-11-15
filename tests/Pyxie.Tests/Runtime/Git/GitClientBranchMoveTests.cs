using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientBranchMoveTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task IsAncestorAsync_IsTrueForAnAncestorAndForTheSameCommit()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		string tip = await repository.CommitFileAsync("a.txt", "a");

		Assert.True(await repository.Git.IsAncestorAsync(repository.Folder, initial, tip, Token), "An earlier commit is an ancestor of a later one.");
		Assert.True(await repository.Git.IsAncestorAsync(repository.Folder, tip, tip, Token), "A commit is its own ancestor.");
	}

	[Fact]
	public async Task IsAncestorAsync_IsFalseForADescendantAndForDivergedCommits()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");

		Assert.False(await repository.Git.IsAncestorAsync(repository.Folder, divergence.MainTip, initial, Token), "A later commit is not an ancestor of an earlier one.");
		Assert.False(await repository.Git.IsAncestorAsync(repository.Folder, divergence.MainTip, divergence.FeatureTip, Token), "Diverged commits are not ancestors of each other.");
		Assert.False(await repository.Git.IsAncestorAsync(repository.Folder, divergence.FeatureTip, divergence.MainTip, Token), "Diverged commits are not ancestors of each other.");
	}

	[Fact]
	public async Task IsAncestorAsync_ForARevisionThatDoesNotExist_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.IsAncestorAsync(repository.Folder, "main", "no-such-commit", Token));

		Assert.Contains("git merge-base failed", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CommitTreeAsync_WritesACommitWithTheTreeAndParentsWithoutMovingAnyBranch()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		string tree = await repository.GetHashAsync($"{divergence.FeatureTip}^{{tree}}");

		string commit = await repository.Git.CommitTreeAsync(repository.Folder, tree, [divergence.MainTip, divergence.FeatureTip], "Merge written without a checkout", Token);

		Assert.Equal($"{divergence.MainTip} {divergence.FeatureTip}", await repository.RunGitAsync("log", "-1", "--format=%P", commit));
		Assert.Equal("Merge written without a checkout", await repository.RunGitAsync("log", "-1", "--format=%s", commit));
		Assert.Equal(tree, await repository.GetHashAsync($"{commit}^{{tree}}"));
		Assert.Equal(divergence.MainTip, await repository.GetHashAsync("main"));
		Assert.Equal(divergence.FeatureTip, await repository.GetHashAsync("feature"));
		Assert.Empty(await WorkingTreeStatus.ListAsync(repository.Folder));
	}

	[Fact]
	public async Task CommitTreeAsync_AuthorsAndCommitsAsPyxieWhateverIdentityTheRepositoryHasConfigured()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		// git takes an author or committer from author.* and committer.* before user.*, so these discriminate a commit made only with -c user.*.
		await repository.RunGitAsync("config", "author.name", "Configured author");
		await repository.RunGitAsync("config", "author.email", "author@configured.invalid");
		await repository.RunGitAsync("config", "committer.name", "Configured committer");
		await repository.RunGitAsync("config", "committer.email", "committer@configured.invalid");
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		string tree = await repository.GetHashAsync($"{divergence.FeatureTip}^{{tree}}");

		string commit = await repository.Git.CommitTreeAsync(repository.Folder, tree, [divergence.MainTip, divergence.FeatureTip], "Merge written without a checkout", Token);

		Assert.Equal(TempGitRepository.AuthorName, await repository.RunGitAsync("config", "user.name"));
		Assert.Equal("Pyxie|pyxie@localhost|Pyxie|pyxie@localhost", await repository.RunGitAsync("log", "-1", "--format=%an|%ae|%cn|%ce", commit));
	}

	[Fact]
	public async Task TryMoveBranchAsync_MovesTheBranchWhenItStillPointsWhereTheCallerSawIt()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		string commit = await WriteMergeCommitAsync(repository, divergence);

		bool moved = await repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token);

		Assert.True(moved, "The branch pointed at the expected commit, so it must move.");
		Assert.Equal(commit, await repository.GetHashAsync("feature"));
	}

	[Fact]
	public async Task TryMoveBranchAsync_RefusesWhenTheBranchMovedMeanwhile()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		string commit = await WriteMergeCommitAsync(repository, divergence);
		await repository.CheckoutAsync("feature");
		string movedMeanwhile = await repository.CommitFileAsync("someone-else.txt", "pushed while the caller worked");
		await repository.CheckoutAsync("main");

		bool moved = await repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token);

		Assert.False(moved, "The branch no longer points at the expected commit, so the move is refused.");
		Assert.Equal(movedMeanwhile, await repository.GetHashAsync("feature"));
	}

	[Fact]
	public async Task TryMoveBranchAsync_ForABranchThatDoesNotExist_ThrowsAGitExceptionAndDoesNotCreateIt()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string head = await repository.GetHashAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.TryMoveBranchAsync(repository.Folder, "no-such-branch", head, head, Token));

		Assert.Contains("git update-ref failed", failure.Message, StringComparison.Ordinal);
		Assert.False(await repository.Git.BranchExistsAsync(repository.Folder, "no-such-branch", Token), "A refused move must not create the branch.");
	}

	private static async Task<string> WriteMergeCommitAsync(TempGitRepository repository, Divergence divergence)
	{
		string tree = await repository.GetHashAsync($"{divergence.FeatureTip}^{{tree}}");

		return await repository.Git.CommitTreeAsync(repository.Folder, tree, [divergence.FeatureTip, divergence.MainTip], "Merge main into feature", Token);
	}
}
