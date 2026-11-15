using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A card whose target branch moved on during its run gets the new commits merged into its branch, goes back through the gates (the combination is untested) without a second review, and is then merged. The reviewer sleeps while the test pushes to <c>main</c> as a person would, which is how main moves on in the middle of a card's run.</summary>
public sealed class PipelineOrchestratorOutdatedTests
{
	// Long enough for the test to commit in its clone and push to main (several git commands, slower on a busy machine) while the reviewer is still asleep.
	private const string ReviewSleepMilliseconds = "8000";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardWhoseMainMovedOnDuringItsReview_IsRunThroughTheGatesAgainWithoutASecondReviewAndMerged()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(harness.Clone);
		string reviewStarted = Path.Combine(harness.ScriptsFolder, "review-started.jsonl");
		await harness.AddGateAsync("Check", "--exit", "0");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent reviewer = await harness.AddReviewerAsync("Reviewer", "--record", reviewStarted, "--sleep", ReviewSleepMilliseconds, "--outcome", "approved");
		WorkCard card = await harness.AddCardAsync("Add the feature", worker, reviewer);
		await harness.StartPipelineAsync();
		await harness.WaitUntilAsync(() => Task.FromResult(File.Exists(reviewStarted)), "the reviewer to start", RuntimeTestTimeouts.Wait);

		string movedMain = await person.CommitFileAsync("other.txt", "from main");
		await person.PushAsync("main");
		BoardCard finished = await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Passed,Review:Approved,Merge:Outdated,Gate:Passed,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal((1, 2, 1), (finished.Card.Attempt, finished.History.GateRounds, finished.History.ReviewRounds));
		Assert.Contains("had moved on", rounds[3].Note, StringComparison.Ordinal);
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal("from main", await harness.Clone.ReadFileAtAsync("main", "other.txt"));
		Assert.Equal(movedMain, (await harness.Clone.RunGitAsync("rev-list", "--parents", "-n", "1", "main")).Split(' ')[1]);
		Assert.True(await harness.Git.IsAncestorAsync(harness.Clone.Folder, movedMain, "main", Token), "The merge must land on top of the commit that was pushed while the card was in review.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardWhoseMainMovedOnDuringItsReview_DoesNotUseAnAttemptForTheOutdatedMerge()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(harness.Clone);
		string reviewStarted = Path.Combine(harness.ScriptsFolder, "review-started.jsonl");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent reviewer = await harness.AddReviewerAsync("Reviewer", "--record", reviewStarted, "--sleep", ReviewSleepMilliseconds, "--outcome", "approved");

		// With a single attempt a setback would stop the card, so only a merge round that is outdated without costing an attempt lets it finish.
		WorkCard card = await harness.AddCardAsync("Add the feature", worker, reviewer, attemptLimit: 1);
		await harness.StartPipelineAsync();
		await harness.WaitUntilAsync(() => Task.FromResult(File.Exists(reviewStarted)), "the reviewer to start", RuntimeTestTimeouts.Wait);

		await person.CommitFileAsync("other.txt", "from main");
		await person.PushAsync("main");
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Skipped,Review:Approved,Merge:Outdated,Gate:Skipped,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.All(rounds, round => Assert.Equal(1, round.Attempt));
	}
}
