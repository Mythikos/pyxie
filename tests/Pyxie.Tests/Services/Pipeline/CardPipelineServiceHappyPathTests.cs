using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A card that works, passes the gates, is approved, and merges moves through every lane and ends in Complete.</summary>
public sealed class CardPipelineServiceHappyPathTests
{
	private static async Task<(PipelineRig Rig, WorkCard Card)> ArrangeAsync()
	{
		PipelineRig rig = new();
		await rig.AddGateAsync();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Rita", canWork: false, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card", worker.Id, reviewer.Id, "Make the login accept plus signs.");

		return (rig, card);
	}

	[Fact]
	public async Task RecordResultAsync_MovesTheCardThroughEveryLaneAndOpensEachNextRound()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work = await rig.ClaimAsync();
			Assert.Equal(CardLane.InProgress, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
			Assert.Equal("Card", work.CardName);
			Assert.Equal(1, work.Attempt);

			PipelineWork gate = await rig.SucceedAsync(work, "Implemented the fix");
			Assert.Equal(RoundKind.Gate, gate.Kind);
			Assert.Equal(2, gate.Sequence);
			Assert.Equal(1, gate.Attempt);
			Assert.Equal(card.Id, gate.CardId);
			Assert.Equal("Card", gate.CardName);
			Assert.Null(gate.Agent);
			Assert.Null(gate.Briefing);
			Assert.Equal(CardLane.Gates, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);

			PipelineWork review = await rig.SucceedAsync(gate, "42 tests passed");
			Assert.Equal(RoundKind.Review, review.Kind);
			Assert.Equal(3, review.Sequence);
			Assert.Equal("Rita", review.Agent?.Name);
			Assert.NotNull(review.Briefing);
			Assert.Equal(CardLane.Review, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);

			PipelineWork merge = await rig.SucceedAsync(review, "Looks good");
			Assert.Equal(RoundKind.Merge, merge.Kind);
			Assert.Equal(4, merge.Sequence);
			Assert.Equal(1, merge.Attempt);
			Assert.Null(merge.Agent);
			Assert.Null(merge.Briefing);
			Assert.Equal(CardLane.Review, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);

			PipelineWork? done = await rig.RecordAsync(merge, RoundResult.Succeeded, report: "Merged into main");
			Assert.Null(done);
			Assert.Equal(CardLane.Complete, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
		}
	}

	[Fact]
	public async Task RecordResultAsync_RecordsEachRoundWithItsOutcomeReportAgentAttemptAndTimes()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			DateTime workStarted = rig.Database.Clock.Tick();
			PipelineWork work = await rig.ClaimAsync();
			DateTime gateStarted = rig.Database.Clock.Tick();
			PipelineWork gate = await rig.SucceedAsync(work, "Implemented the fix");
			DateTime reviewStarted = rig.Database.Clock.Tick();
			PipelineWork review = await rig.SucceedAsync(gate, "42 tests passed");
			DateTime mergeStarted = rig.Database.Clock.Tick();
			PipelineWork merge = await rig.SucceedAsync(review, "Looks good");
			DateTime merged = rig.Database.Clock.Tick();
			await rig.RecordAsync(merge, RoundResult.Succeeded, report: "Merged into main");

			List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);

			Assert.Equal([RoundKind.Work, RoundKind.Gate, RoundKind.Review, RoundKind.Merge], rounds.Select(round => round.Kind));
			Assert.Equal([1, 2, 3, 4], rounds.Select(round => round.Sequence));
			Assert.Equal([1, 1, 1, 1], rounds.Select(round => round.Attempt));
			Assert.Equal([RoundOutcome.Completed, RoundOutcome.Passed, RoundOutcome.Approved, RoundOutcome.Merged], rounds.Select(round => round.Outcome));
			Assert.Equal(["Implemented the fix", "42 tests passed", "Looks good", "Merged into main"], rounds.Select(round => round.Report));
			Assert.Equal(["Wanda", null, "Rita", null], rounds.Select(round => round.Agent?.Name));
			Assert.All(rounds, round => Assert.Null(round.Note));
			Assert.Equal([workStarted, gateStarted, reviewStarted, mergeStarted], rounds.Select(round => round.StartedUtc));
			Assert.Equal([gateStarted, reviewStarted, mergeStarted, merged], rounds.Select(round => round.EndedUtc));
			Assert.Equal([work.RoundId, gate.RoundId, review.RoundId, merge.RoundId], rounds.Select(round => round.Id));
		}
	}

	[Fact]
	public async Task RecordResultAsync_IncrementsTheCardVersionOncePerChange()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			Assert.Equal(0, (await TestData.FindCardAsync(rig.Database, card.Id))?.Version);

			PipelineWork work = await rig.ClaimAsync();
			Assert.Equal(1, (await TestData.FindCardAsync(rig.Database, card.Id))?.Version);

			PipelineWork gate = await rig.SucceedAsync(work);
			Assert.Equal(2, (await TestData.FindCardAsync(rig.Database, card.Id))?.Version);

			PipelineWork review = await rig.SucceedAsync(gate);
			Assert.Equal(3, (await TestData.FindCardAsync(rig.Database, card.Id))?.Version);

			PipelineWork merge = await rig.SucceedAsync(review);
			Assert.Equal(4, (await TestData.FindCardAsync(rig.Database, card.Id))?.Version);

			DateTime lastChange = rig.Database.Clock.Tick();
			await rig.RecordAsync(merge, RoundResult.Succeeded);
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(5, stored.Version);
			Assert.Equal(lastChange, stored.UpdatedUtc);
		}
	}

	[Fact]
	public async Task RecordResultAsync_LeavesTheCardReadyOnItsFirstAttemptWithNoResumeStageWhenItCompletes()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			await rig.CompleteAsync(await rig.ClaimAsync());

			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);

			Assert.NotNull(stored);
			Assert.Equal(CardLane.Complete, stored.Lane);
			Assert.Equal(CardState.Ready, stored.State);
			Assert.Equal(1, stored.Attempt);
			Assert.Null(stored.ResumeRound);
		}
	}

	[Fact]
	public async Task RecordResultAsync_PutsTheCardAtTheBottomOfEachLaneItEntersAndClosesTheGapItLeaves()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			await TestData.AddCardAsync(rig.Database, "Waiting in gates", CardLane.Gates);
			await TestData.AddCardAsync(rig.Database, "Waiting in review", CardLane.Review);
			await TestData.AddCardAsync(rig.Database, "Already done", CardLane.Complete);

			PipelineWork work = await rig.ClaimAsync();
			PipelineWork gate = await rig.SucceedAsync(work);
			Assert.Equal("Waiting in gates,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Gates));
			Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Gates));
			Assert.Empty(await TestData.ReadLaneAsync(rig.Database, CardLane.InProgress));

			PipelineWork review = await rig.SucceedAsync(gate);
			Assert.Equal("Waiting in review,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
			Assert.Equal("Waiting in gates", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Gates));
			Assert.Equal("0", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Gates));

			PipelineWork merge = await rig.SucceedAsync(review);
			Assert.Equal("Waiting in review,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
			Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Review));

			await rig.RecordAsync(merge, RoundResult.Succeeded);
			Assert.Equal("Already done,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Complete));
			Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Complete));
			Assert.Equal("Waiting in review", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
			Assert.Equal("0", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Review));
			Assert.Equal(card.Id, (await TestData.ReadLaneAsync(rig.Database, CardLane.Complete))[1].Id);
		}
	}

	[Fact]
	public async Task RecordResultAsync_GivesTheReviewerTheWorkReportAndTheGateReport()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work = await rig.ClaimAsync();
			PipelineWork gate = await rig.SucceedAsync(work, "Implemented the fix in LoginForm");

			PipelineWork review = await rig.SucceedAsync(gate, "42 tests passed, no warnings");

			Assert.NotNull(review.Briefing);
			Assert.Contains($"# Card {card.Id}: Card", review.Briefing, StringComparison.Ordinal);
			Assert.Contains("Make the login accept plus signs.", review.Briefing, StringComparison.Ordinal);
			Assert.Contains("- Stage: Review (attempt 1 of 5; round 3 of this card)", review.Briefing, StringComparison.Ordinal);
			Assert.Contains("Implemented the fix in LoginForm", review.Briefing, StringComparison.Ordinal);
			Assert.Contains("42 tests passed, no warnings", review.Briefing, StringComparison.Ordinal);
			Assert.Contains($"`{PipelineContract.BranchName(card.Id)}`", review.Briefing, StringComparison.Ordinal);
			Assert.Contains("`main`", review.Briefing, StringComparison.Ordinal);
			Assert.Equal(review.Briefing, (await TestData.ReadRoundsAsync(rig.Database, card.Id))[2].Briefing);
		}
	}

	[Fact]
	public async Task RecordResultAsync_ReturnsTheStoredNextRound()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work = await rig.ClaimAsync();

			PipelineWork gate = await rig.SucceedAsync(work);

			CardRound stored = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[1];
			Assert.Equal(stored.Id, gate.RoundId);
			Assert.Equal(stored.Sequence, gate.Sequence);
			Assert.Equal(stored.Kind, gate.Kind);
			Assert.Equal(stored.Attempt, gate.Attempt);
			Assert.Null(stored.Outcome);
			Assert.Null(stored.EndedUtc);
		}
	}

	[Fact]
	public async Task RecordResultAsync_StoresATrimmedNoteAndNoReportForABlankOne()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work = await rig.ClaimAsync();

			await rig.RecordAsync(work, RoundResult.Succeeded, note: "  Done, with a caveat  ", report: "   ");

			CardRound stored = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[0];
			Assert.Equal("Done, with a caveat", stored.Note);
			Assert.Null(stored.Report);
		}
	}

	[Fact]
	public async Task RecordResultAsync_KeepsTheReportExactlyAsTheRoundProducedIt()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work = await rig.ClaimAsync();
			PipelineWork gate = await rig.SucceedAsync(work);
			string output = "Passed!  TestA\n    indented line\r\nFailed? no\n";

			await rig.RecordAsync(gate, RoundResult.Succeeded, report: output);

			Assert.Equal(output, (await TestData.ReadRoundsAsync(rig.Database, card.Id))[1].Report);
		}
	}

	[Fact]
	public async Task RecordResultAsync_LeavesAMergedCardForPeopleToCloseOut()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			await rig.CompleteAsync(await rig.ClaimAsync());

			ResultAssert.Succeeded(await rig.Cards.CloseAsync(card.Id, TestContext.Current.CancellationToken));

			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardLane.Closed, stored.Lane);
			Assert.Equal(4, (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Count);
		}
	}
}
