using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>An errored round uses an attempt and opens another round of the same kind; a blocked round stops the card in the Queue until a person retries it, and the pipeline never claims a stopped card. The attempt limit and where a stopped card is placed are covered with the rest of the state machine.</summary>
public sealed class CardPipelineServiceErrorTests
{
	private static async Task<PipelineRig> ArrangeAsync()
	{
		PipelineRig rig = new();
		await rig.AddGateAsync();

		return rig;
	}

	[Theory]
	[InlineData(RoundKind.Work, CardLane.InProgress)]
	[InlineData(RoundKind.Gate, CardLane.Gates)]
	[InlineData(RoundKind.Review, CardLane.Review)]
	[InlineData(RoundKind.Merge, CardLane.Review)]
	public async Task RecordResultAsync_OfAnError_RecordsItAndOpensAnotherRoundOfTheSameKindOnTheNextAttempt(RoundKind kind, CardLane lane)
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		int roundsBefore = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Count;

		PipelineWork? next = await rig.RecordAsync(running, RoundResult.Errored, "Harness crashed", "Unhandled exception at line 12");

		Assert.NotNull(next);
		Assert.Equal(kind, next.Kind);
		Assert.Equal(2, next.Attempt);
		Assert.Equal(roundsBefore + 1, next.Sequence);
		Assert.Equal(kind.RunsAgent(), next.Agent is not null);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(lane, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(2, stored.Attempt);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(roundsBefore + 1, rounds.Count);
		CardRound errored = rounds[^2];
		Assert.Equal(running.RoundId, errored.Id);
		Assert.Equal(RoundOutcome.Errored, errored.Outcome);
		Assert.Equal("Harness crashed", errored.Note);
		Assert.Equal("Unhandled exception at line 12", errored.Report);
		Assert.NotNull(errored.EndedUtc);
		Assert.Equal(next.RoundId, rounds[^1].Id);
		Assert.Null(rounds[^1].Outcome);
		Assert.Equal(2, rounds[^1].Attempt);
	}

	[Theory]
	[InlineData(RoundKind.Work, null)]
	[InlineData(RoundKind.Gate, RoundKind.Gate)]
	[InlineData(RoundKind.Review, RoundKind.Review)]
	[InlineData(RoundKind.Merge, RoundKind.Merge)]
	public async Task RecordResultAsync_OfABlock_StopsTheCardInTheQueueInErrorResumingAtTheBlockedStage(RoundKind kind, RoundKind? resumeRound)
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);

		PipelineWork? next = await rig.RecordAsync(running, RoundResult.Blocked, "Needs the staging credentials.", "Details");

		Assert.Null(next);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(resumeRound, stored.ResumeRound);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		CardRound blocked = rounds[^1];
		Assert.Equal(running.RoundId, blocked.Id);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal("Needs the staging credentials.", blocked.Note);
		Assert.Equal("Details", blocked.Report);
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
	}

	[Fact]
	public async Task ClaimNextAsync_NeverClaimsAStoppedCard()
	{
		await using PipelineRig rig = await ArrangeAsync();
		await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Blocked, "Needs the staging credentials.");

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ClaimNextAsync_SkipsAStoppedCardAndTakesTheNextOneFromTheQueue()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard stopped = await TestData.AddCardAsync(rig.Database, "Stopped");
		WorkCard waiting = await TestData.AddCardAsync(rig.Database, "Waiting");
		PipelineWork work = await rig.ClaimAsync();
		Assert.Equal(stopped.Id, work.CardId);
		await rig.RecordAsync(work, RoundResult.Blocked, "Needs the staging credentials.");

		PipelineWork next = await rig.ClaimAsync();

		Assert.Equal(waiting.Id, next.CardId);
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, stopped.Id));
	}

	[Fact]
	public async Task RecordResultAsync_AfterAStopStillRefusesTheSameRound()
	{
		await using PipelineRig rig = await ArrangeAsync();
		await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Blocked, "Needs the staging credentials.");

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new(null, null), TestContext.Current.CancellationToken));

		Assert.Contains("no longer open", message, StringComparison.Ordinal);
	}
}
