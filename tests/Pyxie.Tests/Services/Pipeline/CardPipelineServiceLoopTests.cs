using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A failed gate or a rejection sends the card back to In Progress with a new work round on the next attempt whose briefing quotes the reason.</summary>
public sealed class CardPipelineServiceLoopTests
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
	public async Task RecordResultAsync_OfAFailedGate_SendsTheCardBackToInProgressWithANewWorkRound()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);

			PipelineWork? next = await rig.RecordAsync(gate, RoundResult.Failed, "2 tests failed", "FAIL LoginTests.PlusSign\nFAIL LoginTests.Dot");

			Assert.NotNull(next);
			Assert.Equal(RoundKind.Work, next.Kind);
			Assert.Equal(3, next.Sequence);
			Assert.Equal(2, next.Attempt);
			Assert.Equal("Wanda", next.Agent?.Name);
			Assert.NotNull(next.Briefing);
			Assert.Contains("## Why this card came back", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("Round 2, attempt 1, Gates, Failed.", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("2 tests failed", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("FAIL LoginTests.PlusSign\nFAIL LoginTests.Dot", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("- Stage: Work (attempt 2 of 5; round 3 of this card)", next.Briefing, StringComparison.Ordinal);
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardLane.InProgress, stored.Lane);
			Assert.Equal(CardState.Ready, stored.State);
			Assert.Equal(2, stored.Attempt);
			List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
			Assert.Equal(RoundOutcome.Failed, rounds[1].Outcome);
			Assert.Equal("2 tests failed", rounds[1].Note);
			Assert.Equal("FAIL LoginTests.PlusSign\nFAIL LoginTests.Dot", rounds[1].Report);
			Assert.Null(rounds[2].Outcome);
		}
	}

	[Fact]
	public async Task RecordResultAsync_OfARejection_SendsTheCardBackToInProgressWithANewWorkRound()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork review = await rig.ClaimAtAsync(RoundKind.Review);

			PipelineWork? next = await rig.RecordAsync(review, RoundResult.Failed, "Missing a test", "Add a test for plus signs in the domain part too.");

			Assert.NotNull(next);
			Assert.Equal(RoundKind.Work, next.Kind);
			Assert.Equal(4, next.Sequence);
			Assert.Equal(2, next.Attempt);
			Assert.Equal("Wanda", next.Agent?.Name);
			Assert.NotNull(next.Briefing);
			Assert.Contains("## Why this card came back", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("Round 3, attempt 1, Review, Rejected by Rita.", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("Missing a test", next.Briefing, StringComparison.Ordinal);
			Assert.Contains("Add a test for plus signs in the domain part too.", next.Briefing, StringComparison.Ordinal);
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardLane.InProgress, stored.Lane);
			CardRound rejected = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[2];
			Assert.Equal(RoundOutcome.Rejected, rejected.Outcome);
			Assert.Equal("Missing a test", rejected.Note);
		}
	}

	[Fact]
	public async Task RecordResultAsync_OfAFailedGate_PutsTheCardAtTheBottomOfInProgressAndClosesTheGapInGates()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			await TestData.AddCardAsync(rig.Database, "Busy", CardLane.InProgress);
			PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
			await TestData.AddCardAsync(rig.Database, "Behind it", CardLane.Gates);
			Assert.Equal("Card,Behind it", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Gates));

			await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

			Assert.Equal("Busy,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
			Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.InProgress));
			Assert.Equal("Behind it", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Gates));
			Assert.Equal("0", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Gates));
			Assert.Equal(card.Id, (await TestData.ReadLaneAsync(rig.Database, CardLane.InProgress))[1].Id);
		}
	}

	[Fact]
	public async Task RecordResultAsync_CarriesACardThroughSeveralLoopsToTheMerge()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work1 = await rig.ClaimAsync();
			PipelineWork gate2 = await rig.SucceedAsync(work1, "FIRSTATTEMPT");
			PipelineWork? work3 = await rig.RecordAsync(gate2, RoundResult.Failed, "Build failed", "CS1002 expected ;");
			Assert.NotNull(work3);
			PipelineWork gate4 = await rig.SucceedAsync(work3, "SECONDATTEMPT");
			PipelineWork review5 = await rig.SucceedAsync(gate4, "GATEOUTPUT");
			Assert.NotNull(review5.Briefing);
			Assert.Contains("SECONDATTEMPT", review5.Briefing, StringComparison.Ordinal);
			Assert.DoesNotContain("FIRSTATTEMPT", review5.Briefing, StringComparison.Ordinal);
			PipelineWork? work6 = await rig.RecordAsync(review5, RoundResult.Failed, "Handle empty input", "The empty string crashes the parser.");
			Assert.NotNull(work6);
			PipelineWork gate7 = await rig.SucceedAsync(work6, "THIRDATTEMPT");
			PipelineWork review8 = await rig.SucceedAsync(gate7, "GATEOUTPUT2");
			Assert.NotNull(review8.Briefing);
			Assert.Contains("- Stage: Review (attempt 3 of 5; round 8 of this card)", review8.Briefing, StringComparison.Ordinal);
			Assert.Contains("## Previous review findings", review8.Briefing, StringComparison.Ordinal);
			Assert.Contains("The empty string crashes the parser.", review8.Briefing, StringComparison.Ordinal);
			Assert.Contains("THIRDATTEMPT", review8.Briefing, StringComparison.Ordinal);

			PipelineWork merge9 = await rig.SucceedAsync(review8, "All findings addressed");
			Assert.Equal(RoundKind.Merge, merge9.Kind);
			PipelineWork? end = await rig.RecordAsync(merge9, RoundResult.Succeeded, report: "Merged into main");

			Assert.Null(end);
			List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
			Assert.Equal(
				[
					RoundOutcome.Completed,
					RoundOutcome.Failed,
					RoundOutcome.Completed,
					RoundOutcome.Passed,
					RoundOutcome.Rejected,
					RoundOutcome.Completed,
					RoundOutcome.Passed,
					RoundOutcome.Approved,
					RoundOutcome.Merged,
				],
				rounds.Select(round => round.Outcome));
			Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], rounds.Select(round => round.Sequence));
			Assert.Equal([1, 1, 2, 2, 2, 3, 3, 3, 3], rounds.Select(round => round.Attempt));
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardLane.Complete, stored.Lane);
			Assert.Equal(CardState.Ready, stored.State);
			Assert.Equal(3, stored.Attempt);
			BoardCard onBoard = Assert.Single(await rig.Cards.GetBoardAsync(TestContext.Current.CancellationToken));
			Assert.Equal(new CardHistorySummary(3, 3, 2, 0, 2, false, null), onBoard.History);
		}
	}
}
