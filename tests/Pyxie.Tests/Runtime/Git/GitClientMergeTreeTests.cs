using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary><see cref="GitClient.MergeTreeAsync"/> merges two commits the way <c>git merge</c> would, but without a working tree, so the integrator can merge in the hub. It only writes objects: every test also checks that no branch moved and that the working tree, where there is one, was left alone.</summary>
public sealed class GitClientMergeTreeTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task MergeTreeAsync_OfABranchAlreadyContained_IsCleanAndKeepsOurTree()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string behind = await repository.GetHashAsync();
		await repository.CreateBranchAsync("behind");
		string tip = await repository.CommitFileAsync("ahead.txt", "main moved on");

		GitMergeTree merge = await repository.Git.MergeTreeAsync(repository.Folder, tip, behind, Token);

		Assert.True(merge.IsClean, "Merging a commit that is already contained cannot conflict.");
		Assert.Equal(await repository.GetHashAsync("main^{tree}"), merge.Tree);
		await AssertNothingMovedAsync(repository, ("main", tip), ("behind", behind));
	}

	[Fact]
	public async Task MergeTreeAsync_OfABranchAhead_IsCleanAndTakesItsTree()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string mainTip = await repository.GetHashAsync();
		await repository.CreateBranchAsync("ahead");
		await repository.CheckoutAsync("ahead");
		string aheadTip = await repository.CommitFileAsync("ahead.txt", "feature work");
		await repository.CheckoutAsync("main");

		GitMergeTree merge = await repository.Git.MergeTreeAsync(repository.Folder, mainTip, aheadTip, Token);

		Assert.True(merge.IsClean, "Merging a descendant cannot conflict.");
		Assert.Equal(await repository.GetHashAsync("ahead^{tree}"), merge.Tree);
		await AssertNothingMovedAsync(repository, ("main", mainTip), ("ahead", aheadTip));
	}

	[Fact]
	public async Task MergeTreeAsync_OfDivergedBranchesThatMergeCleanly_WritesTheTreeARealMergeWrites()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("shared.txt", "one\ntwo\nthree\nfour\nfive");
		await repository.CreateBranchAsync("feature");
		await repository.WriteFileAsync("shared.txt", "ONE\ntwo\nthree\nfour\nfive");
		string mainTip = await repository.CommitFileAsync("main-only.txt", "from main");
		await repository.CheckoutAsync("feature");
		await repository.WriteFileAsync("shared.txt", "one\ntwo\nthree\nfour\nFIVE");
		string featureTip = await repository.CommitFileAsync("feature-only.txt", "from the feature branch");
		await repository.CheckoutAsync("main");

		GitMergeTree merge = await repository.Git.MergeTreeAsync(repository.Folder, mainTip, featureTip, Token);

		Assert.True(merge.IsClean, "The branches change different lines and different files, so they merge cleanly.");
		Assert.Empty(merge.ConflictedFiles);
		await AssertNothingMovedAsync(repository, ("main", mainTip), ("feature", featureTip));
		Assert.Equal("ONE\ntwo\nthree\nfour\nFIVE", await repository.ReadFileAtAsync(merge.Tree, "shared.txt"));

		await repository.RunGitAsync("merge", "--quiet", "--no-ff", "--no-edit", "feature");
		Assert.Equal(await repository.GetHashAsync("HEAD^{tree}"), merge.Tree);
	}

	[Fact]
	public async Task MergeTreeAsync_InABareRepository_MergesWithoutAWorkingTree()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(source, "feature");
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		GitMergeTree merge = await hub.Repository.Git.MergeTreeAsync(hub.Folder, divergence.MainTip, divergence.FeatureTip, Token);

		Assert.True(merge.IsClean, "Each branch adds a file of its own, so they merge cleanly.");
		Assert.Equal([TempGitRepository.InitialFile, "feature-only.txt", "main-only.txt"], (await hub.Repository.RunGitAsync("ls-tree", "--name-only", merge.Tree)).Split('\n').Order(StringComparer.Ordinal));
		Assert.Equal(refsBefore, await hub.ListRefsAsync());
	}

	[Fact]
	public async Task MergeTreeAsync_OfConflictingBranches_ListsEachConflictedFileOnceAndNoOther()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("doomed.txt", "main deletes this and the feature branch changes it");
		await Divergence.ConflictingAsync(repository, "feature", "first.txt", "docs/second.txt");
		await repository.CheckoutAsync("feature");
		await repository.WriteFileAsync("doomed.txt", "changed on the feature branch");
		string featureTip = await repository.CommitFileAsync("clean.txt", "merges without trouble");
		await repository.CheckoutAsync("main");
		File.Delete(Path.Combine(repository.Folder, "doomed.txt"));
		string mainTip = await repository.CommitAsync("Delete doomed.txt on main");

		GitMergeTree merge = await repository.Git.MergeTreeAsync(repository.Folder, mainTip, featureTip, Token);

		Assert.False(merge.IsClean, "Both branches changed first.txt and docs/second.txt differently, so the merge conflicts.");
		Assert.Equal(["docs/second.txt", "doomed.txt", "first.txt"], merge.ConflictedFiles.Order(StringComparer.Ordinal));
		await AssertNothingMovedAsync(repository, ("main", mainTip), ("feature", featureTip));
	}

	[Fact]
	public async Task MergeTreeAsync_OfConflictingBranches_WritesATreeWithConflictMarkers()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.ConflictingAsync(repository, "feature", "first.txt");

		GitMergeTree merge = await repository.Git.MergeTreeAsync(repository.Folder, divergence.MainTip, divergence.FeatureTip, Token);

		string? merged = await repository.ReadFileAtAsync(merge.Tree, "first.txt");
		Assert.NotNull(merged);
		Assert.Contains("<<<<<<<", merged, StringComparison.Ordinal);
		Assert.Contains(Divergence.MainContent("first.txt"), merged, StringComparison.Ordinal);
		Assert.Contains(Divergence.FeatureContent("first.txt"), merged, StringComparison.Ordinal);
		Assert.Contains(">>>>>>>", merged, StringComparison.Ordinal);
		await AssertNothingMovedAsync(repository, ("main", divergence.MainTip), ("feature", divergence.FeatureTip));
		Assert.Equal(Divergence.MainContent("first.txt"), await File.ReadAllTextAsync(Path.Combine(repository.Folder, "first.txt"), Token));
	}

	[Fact]
	public async Task MergeTreeAsync_ForAConflictedFileWhoseNameHasSpacesAndNonAsciiLetters_ReportsTheNameAsItIs()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.ConflictingAsync(repository, "feature", "docs/my notes äø.txt");

		GitMergeTree merge = await repository.Git.MergeTreeAsync(repository.Folder, divergence.MainTip, divergence.FeatureTip, Token);

		Assert.Equal(["docs/my notes äø.txt"], merge.ConflictedFiles);
	}

	[Fact]
	public async Task MergeTreeAsync_ForARevisionThatDoesNotExist_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string head = await repository.GetHashAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.MergeTreeAsync(repository.Folder, head, "no-such-branch", Token));

		Assert.Contains("git merge-tree failed", failure.Message, StringComparison.Ordinal);
		await AssertNothingMovedAsync(repository, ("main", head));
	}

	[Fact]
	public async Task MergeTreeAsync_OfUnrelatedHistories_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string head = await repository.GetHashAsync();
		string unrelated = await repository.Git.CommitTreeAsync(repository.Folder, await repository.GetHashAsync("main^{tree}"), [], "A root commit of its own", Token);

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.MergeTreeAsync(repository.Folder, head, unrelated, Token));

		Assert.Contains("git merge-tree failed", failure.Message, StringComparison.Ordinal);
		await AssertNothingMovedAsync(repository, ("main", head));
	}

	private static async Task AssertNothingMovedAsync(TempGitRepository repository, params (string Branch, string Tip)[] branches)
	{
		foreach ((string branch, string tip) in branches)
		{
			string current = await repository.GetHashAsync(branch);
			Assert.True(tip == current, $"{branch} must not move, since merge-tree only writes objects: it was at {tip} and is now at {current}.");
		}

		Assert.Equal("main", await repository.GetCurrentBranchAsync());
		Assert.False((await repository.TryRunGitAsync("rev-parse", "--verify", "--quiet", "MERGE_HEAD")).Succeeded, "merge-tree starts no merge in the working tree.");
		Assert.Empty(await WorkingTreeStatus.ListAsync(repository.Folder));
	}
}
