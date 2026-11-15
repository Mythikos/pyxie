using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary><c>git update-ref</c> fails the same way when the branch moved and when anything else is wrong, so <see cref="GitClient.TryMoveBranchAsync"/> reads the branch back: it answers <see langword="false"/> only when the branch really moved, and throws for every other failure, so a lock file or a permission error is not mistaken for a lost race forever.</summary>
public sealed class GitClientBranchMoveFailureTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<(Divergence Divergence, string MergeCommit)> ArrangeAsync(TempGitRepository repository)
	{
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		string tree = await repository.GetHashAsync($"{divergence.FeatureTip}^{{tree}}");
		string commit = await repository.Git.CommitTreeAsync(repository.Folder, tree, [divergence.FeatureTip, divergence.MainTip], "Merge main into feature", Token);

		return (divergence, commit);
	}

	[Fact]
	public async Task TryMoveBranchAsync_WhenTheBranchsRefIsLockedByAFile_ThrowsInsteadOfReportingThatTheBranchMoved()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		(Divergence divergence, string commit) = await ArrangeAsync(repository);
		await File.WriteAllTextAsync(Path.Combine(repository.Folder, ".git", "refs", "heads", "feature.lock"), string.Empty, Token);

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token));

		Assert.Contains("update-ref", failure.Message, StringComparison.Ordinal);
		Assert.Equal(divergence.FeatureTip, await repository.GetHashAsync("feature"));
	}

	[Fact]
	public async Task TryMoveBranchAsync_OnceTheLockIsGone_MovesTheBranch()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		(Divergence divergence, string commit) = await ArrangeAsync(repository);
		string lockFile = Path.Combine(repository.Folder, ".git", "refs", "heads", "feature.lock");
		await File.WriteAllTextAsync(lockFile, string.Empty, Token);
		await Assert.ThrowsAsync<GitException>(() => repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token));
		File.Delete(lockFile);

		bool moved = await repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token);

		Assert.True(moved, "With the lock gone the branch still pointed where the caller saw it, so it moves.");
		Assert.Equal(commit, await repository.GetHashAsync("feature"));
	}

	[Fact]
	public async Task TryMoveBranchAsync_WhenTheBranchReallyMovedMeanwhile_StillReturnsFalseAndLeavesItWhereItIs()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		(Divergence divergence, string commit) = await ArrangeAsync(repository);
		await repository.RunGitAsync("update-ref", "refs/heads/feature", divergence.MainTip);

		bool moved = await repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token);

		Assert.False(moved, "The branch moved after the caller read it, which is a lost race and not a failure.");
		Assert.Equal(divergence.MainTip, await repository.GetHashAsync("feature"));
	}

	[Fact]
	public async Task TryMoveBranchAsync_WhenTheBranchNoLongerExists_Throws()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		(Divergence divergence, string commit) = await ArrangeAsync(repository);
		await repository.RunGitAsync("branch", "-D", "feature");

		await Assert.ThrowsAsync<GitException>(() => repository.Git.TryMoveBranchAsync(repository.Folder, "feature", commit, divergence.FeatureTip, Token));

		Assert.False(await repository.Git.BranchExistsAsync(repository.Folder, "feature", Token), "A branch that is gone is not recreated.");
	}
}
