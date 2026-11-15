using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>An outdated merge means the target branch moved on and merged cleanly, so the combination has not been tested: the card goes back through the gates on the same attempt. Work that was already approved goes straight to the merge afterwards, without another review, until a new work round resets the approval.</summary>
public sealed class CardPipelineServiceOutdatedTests
{
	[Fact]
	public async Task RecordResultAsync_OfAnOutdatedMerge_OpensAGateRoundOnTheSameAttemptInGatesAndKeepsTheMergeAsRecorded()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await TestData.AddCardAsync(rig.Database, "Busy", CardLane.Gates);
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await PipelineArrangement.ClaimAtAttemptAsync(rig, RoundKind.Merge, attempt: 2);
		await TestData.AddCardAsync(rig.Database, "Behind", CardLane.Review);

		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated, "main moved on", "Merged main into the card's branch.");

		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
		Assert.Equal(2, gate.Attempt);
		Assert.Equal(merge.Sequence + 1, gate.Sequence);
		Assert.Null(gate.Agent);
		Assert.Null(gate.Briefing);
		Assert.Equal(["Merge@2:Outdated", "Gate@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		CardRound outdated = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^2];
		Assert.Equal(merge.RoundId, outdated.Id);
		Assert.Equal("main moved on", outdated.Note);
		Assert.Equal("Merged main into the card's branch.", outdated.Report);
		Assert.NotNull(outdated.EndedUtc);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Gates, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal("Busy,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Gates));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Gates));
		Assert.Equal("Behind", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
		Assert.Equal("0", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Review));
	}

	[Fact]
	public async Task RecordResultAsync_OfAnOutdatedMergeWithoutANote_IsAccepted()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);

		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated);

		Assert.NotNull(gate);
		CardRound outdated = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^2];
		Assert.Equal(RoundOutcome.Outdated, outdated.Outcome);
		Assert.Null(outdated.Note);
	}

	[Fact]
	public async Task RecordResultAsync_OfAGatePassAfterAnApproval_OpensTheMergeAgainWithoutASecondReview()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated, "main moved on");
		Assert.NotNull(gate);

		PipelineWork? again = await rig.RecordAsync(gate, RoundResult.Succeeded, report: "42 tests passed");

		Assert.NotNull(again);
		Assert.Equal(RoundKind.Merge, again.Kind);
		Assert.Equal(1, again.Attempt);
		Assert.Equal(
			["Work@1:Completed", "Gate@1:Passed", "Review@1:Approved", "Merge@1:Outdated", "Gate@1:Passed", "Merge@1:Open"],
			await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);
	}

	[Fact]
	public async Task RecordResultAsync_OfAGatePassAfterNewWork_OpensTheReviewAgainBecauseAConflictResetsTheApproval()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated, "main moved on");
		Assert.NotNull(gate);
		PipelineWork secondMerge = await rig.SucceedAsync(gate);
		PipelineWork? work = await rig.RecordAsync(secondMerge, RoundResult.Failed, "Conflicts in src/Login.cs");
		Assert.NotNull(work);
		PipelineWork newGate = await rig.SucceedAsync(work);

		PipelineWork? next = await rig.RecordAsync(newGate, RoundResult.Succeeded);

		Assert.NotNull(next);
		Assert.Equal(RoundKind.Review, next.Kind);
		Assert.Equal(2, next.Attempt);
		Assert.Equal(
			[
				"Work@1:Completed",
				"Gate@1:Passed",
				"Review@1:Approved",
				"Merge@1:Outdated",
				"Gate@1:Passed",
				"Merge@1:Conflicted",
				"Work@2:Completed",
				"Gate@2:Passed",
				"Review@2:Open",
			],
			await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task RecordResultAsync_OfAGatePassAfterAFailedRecheck_OpensTheReviewAgainBecauseTheNewWorkResetsTheApproval()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated, "main moved on");
		Assert.NotNull(gate);
		PipelineWork? work = await rig.RecordAsync(gate, RoundResult.Failed, "The merged code does not build");
		Assert.NotNull(work);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(2, work.Attempt);
		PipelineWork newGate = await rig.SucceedAsync(work);

		PipelineWork? next = await rig.RecordAsync(newGate, RoundResult.Succeeded);

		Assert.NotNull(next);
		Assert.Equal(RoundKind.Review, next.Kind);
		Assert.Equal(
			["Merge@1:Outdated", "Gate@1:Failed", "Work@2:Completed", "Gate@2:Passed", "Review@2:Open"],
			(await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(5));
	}

	[Fact]
	public async Task RecordResultAsync_OfAGatePassAfterAnOutdatedMergeForACardWithoutAReviewer_OpensTheMergeWithoutASecondSkippedReview()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated, "main moved on");
		Assert.NotNull(gate);

		PipelineWork? again = await rig.RecordAsync(gate, RoundResult.Succeeded);

		Assert.NotNull(again);
		Assert.Equal(RoundKind.Merge, again.Kind);
		Assert.Equal(
			["Work@1:Completed", "Gate@1:Passed", "Review@1:Skipped", "Merge@1:Outdated", "Gate@1:Passed", "Merge@1:Open"],
			await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task RecordResultAsync_OfAnOutdatedMergeWhenNoGateIsEnabled_RecordsTheGateStageSkippedAndOpensTheMergeAgain()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		DateTime recordedAt = rig.Database.Clock.Tick();

		PipelineWork? again = await rig.RecordAsync(merge, RoundResult.Outdated, "main moved on");

		Assert.NotNull(again);
		Assert.Equal(RoundKind.Merge, again.Kind);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(
			["Work@1:Completed", "Gate@1:Skipped", "Review@1:Approved", "Merge@1:Outdated", "Gate@1:Skipped", "Merge@1:Open"],
			rounds.Select(PipelineRoundLog.Describe));
		Assert.Equal(PipelineRules.NoGatesNote, rounds[4].Note);
		Assert.Equal(recordedAt, rounds[4].StartedUtc);
		Assert.Equal(recordedAt, rounds[4].EndedUtc);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	public async Task RecordResultAsync_OfAnOutdatedResultFromARoundThatIsNotAMerge_IsRefusedWithTheReasonAndTheRoundStaysOpen(RoundKind kind)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		RoundReport report = new("main moved on", null);
		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(running.RoundId, RoundResult.Outdated, report, TestContext.Current.CancellationToken));

		Assert.Equal("Only a merge round can be outdated.", message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		CardRound open = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(running.RoundId, open.Id);
		Assert.Null(open.Outcome);
		PipelineWork? next = await rig.RecordAsync(running, RoundResult.Succeeded);
		Assert.NotNull(next);
	}
}
