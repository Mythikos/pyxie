using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A stage that does not apply is recorded as a skipped round with the reason, with no agent, and the card moves on as if it had passed: gates are skipped while none is enabled, and the review while the card has no reviewer.</summary>
public sealed class CardPipelineServiceSkipTests
{
	private static void AssertSkipped(CardRound round, RoundKind kind, string note, DateTime at)
	{
		Assert.Equal(kind, round.Kind);
		Assert.Equal(1, round.Attempt);
		Assert.Equal(RoundOutcome.Skipped, round.Outcome);
		Assert.Equal(note, round.Note);
		Assert.Null(round.Agent);
		Assert.Null(round.Briefing);
		Assert.Null(round.Report);
		Assert.Equal(at, round.StartedUtc);
		Assert.Equal(at, round.EndedUtc);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RecordResultAsync_OfWorkWithoutAnEnabledGate_RecordsTheGateStageSkippedAndOpensTheReview(bool hasDisabledGate)
	{
		await using PipelineRig rig = new();
		if (hasDisabledGate)
		{
			await rig.AddGateAsync(isEnabled: false);
		}

		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork work = await rig.ClaimAsync();
		DateTime recordedAt = rig.Database.Clock.Tick();

		PipelineWork? review = await rig.RecordAsync(work, RoundResult.Succeeded, report: "Done");

		Assert.NotNull(review);
		Assert.Equal(RoundKind.Review, review.Kind);
		Assert.Equal(3, review.Sequence);
		Assert.Equal(1, review.Attempt);
		Assert.NotNull(review.Agent);
		Assert.NotNull(review.Briefing);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(["Work@1:Completed", "Gate@1:Skipped", "Review@1:Open"], rounds.Select(PipelineRoundLog.Describe));
		AssertSkipped(rounds[1], RoundKind.Gate, PipelineRules.NoGatesNote, recordedAt);
		Assert.Equal("No gates are enabled.", rounds[1].Note);
		Assert.Equal([1, 2, 3], rounds.Select(round => round.Sequence));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);
		Assert.Equal(2, stored.Version);
	}

	[Fact]
	public async Task RecordResultAsync_OfAPassedGateForACardWithoutAReviewer_RecordsTheReviewStageSkippedAndOpensTheMergeInTheReviewLane()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		DateTime recordedAt = rig.Database.Clock.Tick();

		PipelineWork? merge = await rig.RecordAsync(gate, RoundResult.Succeeded, report: "42 tests passed");

		Assert.NotNull(merge);
		Assert.Equal(RoundKind.Merge, merge.Kind);
		Assert.Equal(4, merge.Sequence);
		Assert.Equal(1, merge.Attempt);
		Assert.Null(merge.Agent);
		Assert.Null(merge.Briefing);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(["Work@1:Completed", "Gate@1:Passed", "Review@1:Skipped", "Merge@1:Open"], rounds.Select(PipelineRoundLog.Describe));
		AssertSkipped(rounds[2], RoundKind.Review, PipelineRules.NoReviewerNote, recordedAt);
		Assert.Equal("No reviewer is assigned.", rounds[2].Note);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);
		Assert.Equal(3, stored.Version);
	}

	[Fact]
	public async Task RecordResultAsync_OfWorkForACardWithNeitherGatesNorAReviewer_RecordsBothStagesSkippedAndOpensTheMergeThatCompletesTheCard()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		DateTime recordedAt = rig.Database.Clock.Tick();

		PipelineWork? merge = await rig.RecordAsync(work, RoundResult.Succeeded, report: "Done");

		Assert.NotNull(merge);
		Assert.Equal(RoundKind.Merge, merge.Kind);
		Assert.Equal(4, merge.Sequence);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(["Work@1:Completed", "Gate@1:Skipped", "Review@1:Skipped", "Merge@1:Open"], rounds.Select(PipelineRoundLog.Describe));
		AssertSkipped(rounds[1], RoundKind.Gate, PipelineRules.NoGatesNote, recordedAt);
		AssertSkipped(rounds[2], RoundKind.Review, PipelineRules.NoReviewerNote, recordedAt);
		WorkCard? running = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(running);
		Assert.Equal(CardLane.Review, running.Lane);
		Assert.Equal(2, running.Version);

		PipelineWork? end = await rig.RecordAsync(merge, RoundResult.Succeeded);

		Assert.Null(end);
		WorkCard? complete = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(complete);
		Assert.Equal(CardLane.Complete, complete.Lane);
		Assert.Equal(CardState.Ready, complete.State);
	}

	[Fact]
	public async Task RecordResultAsync_OfWork_RunsTheGatesWhenAnyGateIsEnabledEvenBesideADisabledOne()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync("Lint", isEnabled: false);
		await rig.AddGateAsync("Tests", isEnabled: true);
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();

		PipelineWork? gate = await rig.RecordAsync(work, RoundResult.Succeeded);

		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
		Assert.Equal(["Work@1:Completed", "Gate@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task ClaimNextAsync_ResumingAGateRoundAfterTheLastGateWasDisabled_RecordsTheStageSkippedAndOpensTheReview()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(gate.RoundId, "The pipeline was stopped.", TestContext.Current.CancellationToken));
		await PipelineArrangement.DisableGatesAsync(rig);
		DateTime resumedAt = rig.Database.Clock.Tick();

		PipelineWork review = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Review, review.Kind);
		Assert.Equal(1, review.Attempt);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(["Work@1:Completed", "Gate@1:Interrupted", "Gate@1:Skipped", "Review@1:Open"], rounds.Select(PipelineRoundLog.Describe));
		AssertSkipped(rounds[2], RoundKind.Gate, PipelineRules.NoGatesNote, resumedAt);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);
	}
}
