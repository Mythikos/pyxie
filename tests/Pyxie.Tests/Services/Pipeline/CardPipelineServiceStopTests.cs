using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A setback on the card's last attempt stops it instead of opening another round: the card goes to the top of the Queue in Error with its attempt kept and the stage to resume recorded, the lane it left closes the gap, and the pipeline never claims it again until a person retries it.</summary>
public sealed class CardPipelineServiceStopTests
{
	private static async Task<ServiceResult<PipelineWork?>> RecordRawAsync(PipelineRig rig, PipelineWork work, RoundResult result, string note)
	{
		return await rig.Pipeline.RecordResultAsync(work.RoundId, result, new RoundReport(note, "Details"), TestContext.Current.CancellationToken);
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted)]
	public async Task RecordResultAsync_OfASendBackOnTheLastAttempt_StopsTheCardWithoutAnotherRoundAndResumesAtWork(RoundKind kind, RoundOutcome outcome)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 1);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		int roundsBefore = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Count;

		ServiceResult<PipelineWork?> result = await RecordRawAsync(rig, running, RoundResult.Failed, "Sent back");

		Assert.True(result.Succeeded, $"A stop is a success, but got: {result.Error}");
		Assert.Null(result.Value);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(1, stored.Attempt);
		Assert.Null(stored.ResumeRound);
		Assert.Equal(0, stored.SortOrder);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(roundsBefore, rounds.Count);
		Assert.Equal(outcome, rounds[^1].Outcome);
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
	}

	[Theory]
	[InlineData(RoundKind.Work, null)]
	[InlineData(RoundKind.Gate, RoundKind.Gate)]
	[InlineData(RoundKind.Review, RoundKind.Review)]
	[InlineData(RoundKind.Merge, RoundKind.Merge)]
	public async Task RecordResultAsync_OfAnErrorOnTheLastAttempt_StopsTheCardResumingAtTheStageThatKeptErroring(RoundKind kind, RoundKind? resumeRound)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 1);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		int roundsBefore = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Count;

		ServiceResult<PipelineWork?> result = await RecordRawAsync(rig, running, RoundResult.Errored, "Harness crashed");

		Assert.True(result.Succeeded, $"A stop is a success, but got: {result.Error}");
		Assert.Null(result.Value);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(1, stored.Attempt);
		Assert.Equal(resumeRound, stored.ResumeRound);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(roundsBefore, rounds.Count);
		Assert.Equal(RoundOutcome.Errored, rounds[^1].Outcome);
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public async Task RecordResultAsync_OfSetbacksBeforeTheLastAttempt_KeepsGoingAndStopsOnlyOnTheLastOne(int attemptLimit)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", attemptLimit: attemptLimit);
		PipelineWork? work = await rig.ClaimAsync();

		for (int attempt = 1; attempt < attemptLimit; attempt++)
		{
			Assert.NotNull(work);
			PipelineWork gate = await rig.SucceedAsync(work);
			work = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

			Assert.NotNull(work);
			Assert.Equal(attempt + 1, work.Attempt);
			WorkCard? running = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(running);
			Assert.Equal(CardLane.InProgress, running.Lane);
			Assert.Equal(CardState.Ready, running.State);
		}

		Assert.NotNull(work);
		PipelineWork? last = await rig.RecordAsync(await rig.SucceedAsync(work), RoundResult.Failed, "Build failed again");

		Assert.Null(last);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(attemptLimit, stored.Attempt);
		Assert.Equal(attemptLimit, (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Count(round => round.Kind == RoundKind.Work));
	}

	[Fact]
	public async Task RecordResultAsync_OfAStop_PutsTheCardAtTheTopOfTheQueueAboveTheCardsBehindItAndShiftsThemDown()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", attemptLimit: 1);
		WorkCard second = await rig.CreateCardAsync("Second");
		WorkCard third = await rig.CreateCardAsync("Third");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		Assert.Equal(card.Id, gate.CardId);
		Assert.Equal("Second,Third", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));

		await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

		Assert.Equal("Card,Second,Third", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
		foreach (WorkCard waiting in new[] { second, third })
		{
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, waiting.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardState.Ready, stored.State);
			Assert.Equal(waiting.Version, stored.Version);
			Assert.Equal(waiting.UpdatedUtc, stored.UpdatedUtc);
		}
	}

	[Fact]
	public async Task RecordResultAsync_OfAStop_PutsTheCardAboveCardsThatWereStoppedEarlier()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await TestData.AddCardAsync(rig.Database, "Stopped earlier", state: CardState.Error, attempt: 3);
		await rig.CreateCardAsync("Card", attemptLimit: 1);
		await rig.CreateCardAsync("Waiting");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		Assert.Equal("Stopped earlier,Waiting", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));

		await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

		Assert.Equal("Card,Stopped earlier,Waiting", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
	}

	[Theory]
	[InlineData(RoundKind.Work, RoundResult.Errored, CardLane.InProgress)]
	[InlineData(RoundKind.Gate, RoundResult.Failed, CardLane.Gates)]
	[InlineData(RoundKind.Review, RoundResult.Failed, CardLane.Review)]
	[InlineData(RoundKind.Merge, RoundResult.Failed, CardLane.Review)]
	public async Task RecordResultAsync_OfAStop_ClosesTheGapInTheLaneTheCardLeft(RoundKind kind, RoundResult result, CardLane lane)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await TestData.AddCardAsync(rig.Database, "Before", lane);
		await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 1);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		await TestData.AddCardAsync(rig.Database, "After", lane);
		Assert.Equal("Before,Card,After", await TestData.ReadLaneNamesAsync(rig.Database, lane));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(rig.Database, lane));

		PipelineWork? next = await rig.RecordAsync(running, result, "Out of attempts");

		Assert.Null(next);
		Assert.Equal("Before,After", await TestData.ReadLaneNamesAsync(rig.Database, lane));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, lane));
		Assert.Equal("Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
	}

	[Fact]
	public async Task RecordResultAsync_OfAStop_AdvancesTheVersionOnceAndStampsTheChange()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", attemptLimit: 1);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);
		DateTime stoppedAt = rig.Database.Clock.Tick();

		await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(before.Version + 1, stored.Version);
		Assert.Equal(stoppedAt, stored.UpdatedUtc);
		CardRound failed = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(stoppedAt, failed.EndedUtc);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAStop_NeverClaimsTheCardAndTakesTheReadyCardBehindItInstead()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard stopped = await rig.CreateCardAsync("Stopped", attemptLimit: 1);
		WorkCard waiting = await rig.CreateCardAsync("Waiting");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");
		int roundsBefore = (await TestData.ReadRoundsAsync(rig.Database, stopped.Id)).Count;

		PipelineWork next = await rig.ClaimAsync();
		Assert.Equal(waiting.Id, next.CardId);

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		Assert.Equal(roundsBefore, (await TestData.ReadRoundsAsync(rig.Database, stopped.Id)).Count);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(CardLane.Queue, stored.Lane);
	}
}
