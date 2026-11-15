using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>What a merge agent's resolution and the conflict report are checked with: the parents of a commit, and the changes that landed on a branch along its first parent, where each merge is one entry.</summary>
public sealed class GitClientHistoryShapeTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetParentsAsync_ForAMergeCommit_ListsBothParentsInOrder()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		await repository.RunGitAsync("merge", "--no-ff", "--no-edit", "feature");

		IReadOnlyList<string> parents = await repository.Git.GetParentsAsync(repository.Folder, "HEAD", Token);

		Assert.Equal([divergence.MainTip, divergence.FeatureTip], parents);
	}

	[Fact]
	public async Task GetParentsAsync_ForAnOrdinaryCommit_ListsItsOnlyParent()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		await repository.CommitFileAsync("next.txt", "next");

		Assert.Equal([initial], await repository.Git.GetParentsAsync(repository.Folder, "main", Token));
	}

	[Fact]
	public async Task GetParentsAsync_ForTheFirstCommit_ListsNoParent()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Empty(await repository.Git.GetParentsAsync(repository.Folder, "main", Token));
	}

	[Fact]
	public async Task GetParentsAsync_ForACommitThatDoesNotExist_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		await Assert.ThrowsAsync<GitException>(() => repository.Git.GetParentsAsync(repository.Folder, new string('0', 40), Token));
	}

	[Fact]
	public async Task LogFirstParentAsync_ListsAMergeAsOneEntryAndNotTheCommitsItBroughtIn()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string start = await repository.GetHashAsync();
		await repository.CreateBranchAsync("feature");
		await repository.CheckoutAsync("feature");
		await repository.CommitFileAsync("one.txt", "one", "Feature commit one");
		await repository.CommitFileAsync("two.txt", "two", "Feature commit two");
		await repository.CheckoutAsync("main");
		await repository.CommitFileAsync("main.txt", "main", "Main commit");
		await repository.RunGitAsync("merge", "--no-ff", "--message", "Merge the feature", "feature");
		await repository.CommitFileAsync("after.txt", "after", "Commit after the merge");

		IReadOnlyList<GitCommit> landed = await repository.Git.LogFirstParentAsync(repository.Folder, $"{start}..main", skip: 0, 10, Token);

		Assert.Equal(["Commit after the merge", "Merge the feature", "Main commit"], landed.Select(commit => commit.Subject));
	}

	[Fact]
	public async Task LogFirstParentAsync_ForARange_StopsAtWhatTheCardsBranchAlreadyHas()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("known.txt", "known", "Already on the branch");
		await repository.CreateBranchAsync("card");
		await repository.CommitFileAsync("landed-one.txt", "one", "Landed one");
		await repository.CommitFileAsync("landed-two.txt", "two", "Landed two");

		IReadOnlyList<GitCommit> landed = await repository.Git.LogFirstParentAsync(repository.Folder, "card..main", skip: 0, 10, Token);

		Assert.Equal(["Landed two", "Landed one"], landed.Select(commit => commit.Subject));
	}

	[Fact]
	public async Task LogFirstParentAsync_WithATake_ReturnsTheNewestOnes()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await EmptyCommits.AddAsync(repository, "main", 5);

		IReadOnlyList<GitCommit> landed = await repository.Git.LogFirstParentAsync(repository.Folder, "main", skip: 0, 2, Token);

		Assert.Equal(["Commit 5", "Commit 4"], landed.Select(commit => commit.Subject));
	}

	// The first-parent line of main is, newest first: After 2, After 1, Merge the feature, Main commit, Initial commit. Feature two and Feature one are reachable only through the merge's second parent, and a log of every commit would list them after Main commit.
	[Theory]
	[InlineData(0, 2, "After 2,After 1")]
	[InlineData(2, 2, "Merge the feature,Main commit")]
	[InlineData(3, 5, "Main commit,Initial commit")]
	[InlineData(4, 1, "Initial commit")]
	[InlineData(5, 2, "")]
	[InlineData(0, 10, "After 2,After 1,Merge the feature,Main commit,Initial commit")]
	public async Task LogFirstParentAsync_WithASkipAndATake_PagesAlongTheFirstParentAndNeverEntersAMergesSecondParent(int skip, int take, string expected)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("feature");
		await repository.CheckoutAsync("feature");
		await repository.CommitFileAsync("one.txt", "one", "Feature one");
		await repository.CommitFileAsync("two.txt", "two", "Feature two");
		await repository.CheckoutAsync("main");
		await repository.CommitFileAsync("main.txt", "main", "Main commit");
		await repository.RunGitAsync("merge", "--no-ff", "--message", "Merge the feature", "feature");
		await repository.CommitFileAsync("after-1.txt", "1", "After 1");
		await repository.CommitFileAsync("after-2.txt", "2", "After 2");

		IReadOnlyList<GitCommit> page = await repository.Git.LogFirstParentAsync(repository.Folder, "main", skip, take, Token);

		Assert.Equal(expected, string.Join(',', page.Select(commit => commit.Subject)));
	}

	[Fact]
	public async Task LogFirstParentAsync_ForARangeThatHoldsNothingOrDoesNotExist_IsEmpty()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Empty(await repository.Git.LogFirstParentAsync(repository.Folder, "main..main", skip: 0, 10, Token));
		Assert.Empty(await repository.Git.LogFirstParentAsync(repository.Folder, "nothing..main", skip: 0, 10, Token));
	}
}
