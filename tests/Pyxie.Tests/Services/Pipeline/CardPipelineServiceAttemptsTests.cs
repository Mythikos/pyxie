using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>An attempt is used up by every setback: a send-back (a failed gate, a rejection, a merge conflict) opens a new work round on the next attempt, and an error opens a new round of the same kind on the next attempt. Interruptions, skipped stages, and a stale base (an outdated merge) use none. Every round records the attempt it belongs to.</summary>
public sealed class CardPipelineServiceAttemptsTests
{
	// The limit leaves room for the attempts these tests use; stopping at the limit is covered with the stop tests.
	private const int AttemptLimit = 5;

	private static async Task<(PipelineRig Rig, WorkCard Card)> ArrangeAsync(int attemptLimit = AttemptLimit)
	{
		PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: attemptLimit);

		return (rig, card);
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted)]
	public async Task RecordResultAsync_OfASendBack_OpensAWorkRoundOnTheNextAttemptAndKeepsTheFinishedRoundOnTheOldOne(RoundKind kind, RoundOutcome outcome)
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork running = await rig.ClaimAtAsync(kind);
			Assert.Equal(1, running.Attempt);

			PipelineWork? next = await rig.RecordAsync(running, RoundResult.Failed, "Sent back");

			Assert.NotNull(next);
			Assert.Equal(RoundKind.Work, next.Kind);
			Assert.Equal(2, next.Attempt);
			Assert.Equal([$"{kind}@1:{outcome}", "Work@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(2, stored.Attempt);
			Assert.Equal(CardLane.InProgress, stored.Lane);
			Assert.Equal(CardState.Ready, stored.State);
		}
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task RecordResultAsync_OfRepeatedErrors_OpensARoundOfTheSameKindOnTheNextAttemptEachTime(RoundKind kind)
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork first = await rig.ClaimAtAsync(kind);

			PipelineWork? second = await rig.RecordAsync(first, RoundResult.Errored, "Harness crashed");
			Assert.NotNull(second);
			PipelineWork? third = await rig.RecordAsync(second, RoundResult.Errored, "Harness crashed again");

			Assert.NotNull(third);
			Assert.Equal([kind, kind], new[] { second.Kind, third.Kind });
			Assert.Equal([2, 3], new[] { second.Attempt, third.Attempt });
			Assert.Equal([$"{kind}@1:Errored", $"{kind}@2:Errored", $"{kind}@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(3));
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(3, stored.Attempt);
			Assert.Equal(PipelineRules.LaneOf(kind), stored.Lane);
		}
	}

	[Fact]
	public async Task RecordResultAsync_CountsSendBacksAndErrorsOfAnyKindAgainstTheSameAttempts()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work1 = await rig.ClaimAsync();
			PipelineWork gate = await rig.SucceedAsync(work1);
			PipelineWork? work2 = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");
			Assert.NotNull(work2);

			PipelineWork? work3 = await rig.RecordAsync(work2, RoundResult.Errored, "Harness crashed");

			Assert.NotNull(work3);
			Assert.Equal(3, work3.Attempt);
			Assert.Equal(["Work@1:Completed", "Gate@1:Failed", "Work@2:Errored", "Work@3:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		}
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task ClaimNextAsync_AfterAnInterruption_ResumesTheSameAttemptEvenOnTheLastOne(RoundKind kind)
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync(attemptLimit: 2);
		await using (rig)
		{
			PipelineWork running = await PipelineArrangement.ClaimAtAttemptAsync(rig, kind, attempt: 2);

			for (int interruption = 0; interruption < 3; interruption++)
			{
				ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(running.RoundId, "The pipeline was stopped.", TestContext.Current.CancellationToken));
				running = await rig.ClaimAsync();

				Assert.Equal(kind, running.Kind);
				Assert.Equal(2, running.Attempt);
			}

			Assert.Equal([$"{kind}@2:Interrupted", $"{kind}@2:Interrupted", $"{kind}@2:Interrupted", $"{kind}@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(4));
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(2, stored.Attempt);
			Assert.Equal(CardState.Ready, stored.State);
			Assert.Equal(PipelineRules.LaneOf(kind), stored.Lane);
		}
	}

	[Fact]
	public async Task RecordResultAsync_OfSkippedStages_KeepsEveryRoundOnTheFirstAttemptEvenWithALimitOfOne()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card", attemptLimit: 1);
		PipelineWork work = await rig.ClaimAsync();

		PipelineWork? merge = await rig.RecordAsync(work, RoundResult.Succeeded);

		Assert.NotNull(merge);
		Assert.Equal(RoundKind.Merge, merge.Kind);
		Assert.Equal(1, merge.Attempt);
		Assert.Equal(["Work@1:Completed", "Gate@1:Skipped", "Review@1:Skipped", "Merge@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(1, stored.Attempt);
		Assert.Equal(CardLane.Review, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
	}

	[Fact]
	public async Task RecordResultAsync_OfAnOutdatedMerge_StaysOnTheSameAttemptEvenOnTheLastOne()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync(attemptLimit: 2);
		await using (rig)
		{
			PipelineWork merge = await PipelineArrangement.ClaimAtAttemptAsync(rig, RoundKind.Merge, attempt: 2);

			for (int cycle = 0; cycle < 3; cycle++)
			{
				PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated);
				Assert.NotNull(gate);
				Assert.Equal(RoundKind.Gate, gate.Kind);
				Assert.Equal(2, gate.Attempt);
				merge = await rig.SucceedAsync(gate);
				Assert.Equal(RoundKind.Merge, merge.Kind);
				Assert.Equal(2, merge.Attempt);
			}

			Assert.Equal(
				[
					"Merge@2:Outdated",
					"Gate@2:Passed",
					"Merge@2:Outdated",
					"Gate@2:Passed",
					"Merge@2:Outdated",
					"Gate@2:Passed",
					"Merge@2:Open",
				],
				(await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(7));
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(2, stored.Attempt);
			Assert.Equal(CardState.Ready, stored.State);
			Assert.Equal(CardLane.Review, stored.Lane);
		}
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task RecordResultAsync_OfAnErrorWithoutANote_IsRefusedAndUsesNoAttempt(string? note)
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork running = await rig.ClaimAtAsync(RoundKind.Gate);
			WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(before);

			string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(running.RoundId, RoundResult.Errored, new RoundReport(note, "Details"), TestContext.Current.CancellationToken));

			Assert.Equal("A note is required when a round fails, is rejected, conflicts, errors, or is blocked.", message);
			await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
			Assert.Equal(["Work@1:Completed", "Gate@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		}
	}

	[Fact]
	public async Task RecordResultAsync_StampsEveryReturnedRoundWithTheCardsAttempt()
	{
		(PipelineRig rig, WorkCard card) = await ArrangeAsync();
		await using (rig)
		{
			PipelineWork work1 = await rig.ClaimAsync();
			PipelineWork gate = await rig.SucceedAsync(work1);
			PipelineWork? work2 = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");
			Assert.NotNull(work2);
			PipelineWork gate2 = await rig.SucceedAsync(work2);
			PipelineWork review = await rig.SucceedAsync(gate2);

			List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);

			Assert.Equal([1, 1, 2, 2, 2], rounds.Select(round => round.Attempt));
			Assert.Equal([work1.Attempt, gate.Attempt, work2.Attempt, gate2.Attempt, review.Attempt], rounds.Select(round => round.Attempt));
		}
	}
}
