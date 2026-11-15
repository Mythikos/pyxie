using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>Rounds still open when Pyxie starts belonged to a process that stopped; they are marked interrupted and the cards resume at the same stage before new work starts.</summary>
public sealed class CardPipelineServiceRecoveryTests
{
	// A rig with an enabled gate, so cards get a gate round. Every card has the default agent as its reviewer, so cards get a review round.
	private static async Task<PipelineRig> ArrangeRigAsync()
	{
		PipelineRig rig = new();
		await rig.AddGateAsync();

		return rig;
	}

	// Four cards: one running each kind of round and one still waiting in the Queue.
	private static async Task<(PipelineRig Rig, WorkCard Working, WorkCard Gating, WorkCard Reviewing, WorkCard Waiting)> ArrangeAsync()
	{
		PipelineRig rig = await ArrangeRigAsync();
		WorkCard working = await TestData.AddCardAsync(rig.Database, "Working", reviewerAgentId: TestData.DefaultAgent);
		WorkCard gating = await TestData.AddCardAsync(rig.Database, "Gating", reviewerAgentId: TestData.DefaultAgent);
		WorkCard reviewing = await TestData.AddCardAsync(rig.Database, "Reviewing", reviewerAgentId: TestData.DefaultAgent);
		WorkCard waiting = await TestData.AddCardAsync(rig.Database, "Waiting", reviewerAgentId: TestData.DefaultAgent);
		await rig.ClaimAtAsync(RoundKind.Work);
		await rig.ClaimAtAsync(RoundKind.Gate);
		await rig.ClaimAtAsync(RoundKind.Review);

		return (rig, working, gating, reviewing, waiting);
	}

	[Fact]
	public async Task RecoverInterruptedAsync_MarksEveryRunningRoundInterruptedAndCountsThem()
	{
		(PipelineRig rig, WorkCard working, WorkCard gating, WorkCard reviewing, _) = await ArrangeAsync();
		await using (rig)
		{
			DateTime recoveredAt = rig.Database.Clock.Tick();

			int interrupted = await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

			Assert.Equal(3, interrupted);
			foreach (WorkCard card in new[] { working, gating, reviewing })
			{
				CardRound last = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
				Assert.Equal(RoundOutcome.Interrupted, last.Outcome);
				Assert.NotNull(last.EndedUtc);
				Assert.Equal(recoveredAt, last.EndedUtc.Value);
				Assert.Contains("Pyxie stopped", last.Note, StringComparison.Ordinal);
			}
		}
	}

	[Fact]
	public async Task RecoverInterruptedAsync_LeavesCardsInTheirLanesAndAdvancesOnlyTheVersionOfInterruptedCards()
	{
		(PipelineRig rig, WorkCard working, WorkCard gating, _, WorkCard waiting) = await ArrangeAsync();
		await using (rig)
		{
			WorkCard? workingBefore = await TestData.FindCardAsync(rig.Database, working.Id);
			WorkCard? gatingBefore = await TestData.FindCardAsync(rig.Database, gating.Id);
			Assert.NotNull(workingBefore);
			Assert.NotNull(gatingBefore);
			WorkCard? waitingBefore = await TestData.FindCardAsync(rig.Database, waiting.Id);
			Assert.NotNull(waitingBefore);

			await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

			WorkCard? workingAfter = await TestData.FindCardAsync(rig.Database, working.Id);
			WorkCard? gatingAfter = await TestData.FindCardAsync(rig.Database, gating.Id);
			Assert.NotNull(workingAfter);
			Assert.NotNull(gatingAfter);
			Assert.Equal(CardLane.InProgress, workingAfter.Lane);
			Assert.Equal(CardLane.Gates, gatingAfter.Lane);
			Assert.Equal(workingBefore.Version + 1, workingAfter.Version);
			Assert.Equal(gatingBefore.Version + 1, gatingAfter.Version);
			Assert.Equal(workingBefore.SortOrder, workingAfter.SortOrder);
			await CardAssert.UnchangedAsync(rig.Database, waitingBefore, expectedCardCount: 4);
		}
	}

	[Fact]
	public async Task RecoverInterruptedAsync_KeepsTheRoundsThatAlreadyEnded()
	{
		await using PipelineRig rig = await ArrangeRigAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		PipelineWork? work = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed", "CS1002");
		Assert.NotNull(work);

		Assert.Equal(1, await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken));

		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal([RoundOutcome.Completed, RoundOutcome.Failed, RoundOutcome.Interrupted], rounds.Select(round => round.Outcome));
		Assert.Equal("Build failed", rounds[1].Note);
		Assert.Equal("CS1002", rounds[1].Report);
	}

	[Fact]
	public async Task RecoverInterruptedAsync_WithNothingRunning_ReturnsZeroAndChangesNothing()
	{
		await using PipelineRig rig = await ArrangeRigAsync();
		WorkCard stopped = await TestData.AddCardAsync(rig.Database, "Stopped");
		WorkCard queued = await TestData.AddCardAsync(rig.Database, "Queued");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Blocked, "Needs the staging credentials.");
		WorkCard? stoppedBefore = await TestData.FindCardAsync(rig.Database, stopped.Id);
		Assert.NotNull(stoppedBefore);
		WorkCard? queuedBefore = await TestData.FindCardAsync(rig.Database, queued.Id);
		Assert.NotNull(queuedBefore);

		int interrupted = await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, interrupted);
		await CardAssert.UnchangedAsync(rig.Database, stoppedBefore, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(rig.Database, queuedBefore, expectedCardCount: 2);
		Assert.Equal(RoundOutcome.Blocked, Assert.Single(await TestData.ReadRoundsAsync(rig.Database, stopped.Id)).Outcome);
	}

	[Fact]
	public async Task ClaimNextAsync_ResumesInterruptedCardsLaterStagesFirstAndThenTakesTheQueue()
	{
		(PipelineRig rig, WorkCard working, WorkCard gating, WorkCard reviewing, WorkCard waiting) = await ArrangeAsync();
		await using (rig)
		{
			await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

			PipelineWork first = await rig.ClaimAsync();
			PipelineWork second = await rig.ClaimAsync();
			PipelineWork third = await rig.ClaimAsync();
			PipelineWork fourth = await rig.ClaimAsync();

			int[] expectedCards = [reviewing.Id, gating.Id, working.Id, waiting.Id];
			int[] claimedCards = [first.CardId, second.CardId, third.CardId, fourth.CardId];
			RoundKind[] expectedKinds = [RoundKind.Review, RoundKind.Gate, RoundKind.Work, RoundKind.Work];
			RoundKind[] claimedKinds = [first.Kind, second.Kind, third.Kind, fourth.Kind];
			Assert.Equal(expectedCards, claimedCards);
			Assert.Equal(expectedKinds, claimedKinds);
			Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		}
	}

	[Fact]
	public async Task ClaimNextAsync_ResumesAtTheSameStageWithTheNextSequenceAndWithoutMovingTheCard()
	{
		(PipelineRig rig, WorkCard working, WorkCard gating, WorkCard reviewing, _) = await ArrangeAsync();
		await using (rig)
		{
			await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

			PipelineWork review = await rig.ClaimAsync();
			PipelineWork gate = await rig.ClaimAsync();
			PipelineWork work = await rig.ClaimAsync();

			Assert.Equal(4, review.Sequence);
			Assert.Equal(3, gate.Sequence);
			Assert.Equal(2, work.Sequence);
			Assert.Equal([1, 1, 1], new[] { review.Attempt, gate.Attempt, work.Attempt });
			Assert.Equal(CardLane.Review, (await TestData.FindCardAsync(rig.Database, reviewing.Id))?.Lane);
			Assert.Equal(CardLane.Gates, (await TestData.FindCardAsync(rig.Database, gating.Id))?.Lane);
			Assert.Equal(CardLane.InProgress, (await TestData.FindCardAsync(rig.Database, working.Id))?.Lane);
			CardRound reopened = (await TestData.ReadRoundsAsync(rig.Database, working.Id))[^1];
			Assert.Equal(work.RoundId, reopened.Id);
			Assert.Null(reopened.Outcome);
		}
	}

	[Fact]
	public async Task ClaimNextAsync_ResumesWithABriefingThatSaysTheLastAttemptWasInterrupted()
	{
		(PipelineRig rig, _, _, _, _) = await ArrangeAsync();
		await using (rig)
		{
			await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

			PipelineWork review = await rig.ClaimAsync();
			PipelineWork gate = await rig.ClaimAsync();
			PipelineWork work = await rig.ClaimAsync();

			Assert.NotNull(work.Briefing);
			Assert.Contains("## How the previous attempt ended", work.Briefing, StringComparison.Ordinal);
			Assert.Contains("Pyxie stopped while this round was running.", work.Briefing, StringComparison.Ordinal);
			Assert.Contains("- Stage: Work (attempt 1 of 5; round 2 of this card)", work.Briefing, StringComparison.Ordinal);
			Assert.NotNull(review.Briefing);
			Assert.Contains("- Stage: Review (attempt 1 of 5; round 4 of this card)", review.Briefing, StringComparison.Ordinal);
			Assert.Null(gate.Briefing);
			Assert.Null(gate.Agent);
		}
	}

	[Fact]
	public async Task ClaimNextAsync_ResumesInterruptedCardsOfOneLaneInQueueOrder()
	{
		await using PipelineRig rig = await ArrangeRigAsync();
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "First", "Second");
		await rig.ClaimAsync();
		await rig.ClaimAsync();
		await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

		PipelineWork first = await rig.ClaimAsync();
		PipelineWork second = await rig.ClaimAsync();

		Assert.Equal(queue["First"].Id, first.CardId);
		Assert.Equal(queue["Second"].Id, second.CardId);
	}

	[Fact]
	public async Task ClaimNextAsync_DoesNotResumeTheSameInterruptedCardTwice()
	{
		await using PipelineRig rig = await ArrangeRigAsync();
		await TestData.AddCardAsync(rig.Database, "Only");
		await rig.ClaimAsync();
		await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(2, resumed.Sequence);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task RecordResultAsync_RefusesTheRoundThatWasMarkedInterruptedAndAcceptsTheResumedOne()
	{
		await using PipelineRig rig = await ArrangeRigAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork original = await rig.ClaimAsync();
		await rig.Pipeline.RecoverInterruptedAsync(TestContext.Current.CancellationToken);
		PipelineWork resumed = await rig.ClaimAsync();

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(original.RoundId, RoundResult.Succeeded, new(null, "Late report"), TestContext.Current.CancellationToken));
		PipelineWork gate = await rig.SucceedAsync(resumed, "Real report");

		Assert.Contains("no longer open", message, StringComparison.Ordinal);
		Assert.Equal(RoundKind.Gate, gate.Kind);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal([RoundOutcome.Interrupted, RoundOutcome.Completed, null], rounds.Select(round => round.Outcome));
		Assert.Null(rounds[0].Report);
		Assert.Equal("Real report", rounds[1].Report);
	}
}
