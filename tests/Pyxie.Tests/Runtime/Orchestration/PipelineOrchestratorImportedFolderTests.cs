using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A folder is imported by cloning it, and the pipeline only ever writes to the clone: finished cards land in the clone's target branch, and the folder a person works in keeps its branches, HEAD, working files, and uncommitted changes exactly as they were, which also means nothing in it can stand in the way of a merge.</summary>
public sealed class PipelineOrchestratorImportedFolderTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardRunToComplete_MergesIntoTheClonesMainAndNeverChangesTheSourceFolder()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("feature/other");
		await source.CommitFileAsync("notes.txt", "line one\r\nline two\nline three");
		await source.WriteFileAsync("notes.txt", "edited after the commit\r\nwith mixed\nline endings\r\n");
		await source.WriteFileAsync("scratch.bin", "\0\u0001\u0002 not text");
		string sourceMain = await source.GetHashAsync("main");
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(source);
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		Repository connected = await harness.ConnectRepositoryAsync(source);
		await harness.AddGateAsync("Check", "--exit", "0");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent reviewer = await harness.AddReviewerAsync("Reviewer", "--outcome", "approved");
		WorkCard card = await harness.AddCardAsync("Add the feature", worker, reviewer);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		Assert.Equal("Work:Completed,Gate:Passed,Review:Approved,Merge:Merged", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		string[] mergeCommit = (await harness.Clone.RunGitAsync("rev-list", "--parents", "-n", "1", "main")).Split(' ');
		Assert.Equal(connected.Path, harness.Clone.Folder);
		Assert.Equal(3, mergeCommit.Length);
		Assert.Equal(sourceMain, mergeCommit[1]);
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));

		await before.AssertUnchangedAsync(source);
		Assert.Equal(sourceMain, await source.GetHashAsync("main"));
		Assert.Equal("main", await source.GetCurrentBranchAsync());
		Assert.Null(await source.ReadFileAtAsync("main", "feature.txt"));
		Assert.False(File.Exists(Path.Combine(source.Folder, "feature.txt")), "The card's file must not appear in the folder that was imported.");
		Assert.Equal(["feature/other", "main"], (await source.Git.ListBranchesAsync(source.Folder, Token)).Select(branch => branch.Name).Order(StringComparer.Ordinal));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task UncommittedChangesInTheSourceFolder_AreNotImportedAndNeverBlockTheMerge()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CommitFileAsync("shared.txt", "committed");
		await source.CommitFileAsync("other.txt", "committed too");

		// Before the import: an edit of a file the card will change, and a staged file the card will also add. Merging these into the folder itself would have been blocked.
		await source.WriteFileAsync("shared.txt", "edited in the source, not committed");
		await source.WriteFileAsync("staged.txt", "staged in the source");
		await source.RunGitAsync("add", "staged.txt");
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);

		// After the import: more changes, which the clone never sees.
		await source.WriteFileAsync("other.txt", "edited after the import");
		await source.WriteFileAsync("scratch.txt", "untracked");
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(source);
		string[] dirty = [.. before.Checkout.Status.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Order(StringComparer.Ordinal)];
		Assert.Equal(["?? scratch.txt", "A  staged.txt", "M other.txt", "M shared.txt"], dirty);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "shared.txt=from the card", "--write", "staged.txt=from the card too");
		WorkCard card = await harness.AddCardAsync("Change the shared files", worker);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		Assert.Equal("Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal("from the card", await harness.Clone.ReadFileAtAsync("main", "shared.txt"));
		Assert.Equal("from the card too", await harness.Clone.ReadFileAtAsync("main", "staged.txt"));
		Assert.Equal("committed too", await harness.Clone.ReadFileAtAsync("main", "other.txt"));
		Assert.Null(await harness.Clone.ReadFileAtAsync("main", "scratch.txt"));
		Assert.True(harness.Clone.IsBare, "The clone is a hub with no working tree for the source's changes to leak into.");
		Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(harness.Clone.Folder).Select(Path.GetFileName), name => name is "shared.txt" or "staged.txt" or "other.txt" or "scratch.txt" or "feature.txt");
		await before.AssertUnchangedAsync(source);
	}
}
