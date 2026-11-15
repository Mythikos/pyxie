using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>
/// Reporting a result races with giving the round back, with another worker reporting the same round, and with agent changes that touch the card.
/// Each race is staged between the operation's read and its conditional write; the stale operation re-reads and acts only on what is really stored.
/// </summary>
public sealed class CardPipelineServiceRecordRaceTests
{
	[Fact]
	public async Task RecordResultAsync_WhenTheRoundIsInterruptedAfterTheRead_RefusesTheStaleRoundAndOpensNothing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork work = await rig.ClaimAsync();
		CardPipelineService other = rig.CreateSecondPipeline();
		interceptor.Arm(async () => ResultAssert.Succeeded(await other.InterruptAsync(work.RoundId, "The pipeline was stopped.", TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Late work"), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Contains("no longer open", message, StringComparison.Ordinal);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal(RoundOutcome.Interrupted, round.Outcome);
		Assert.Equal("The pipeline was stopped.", round.Note);
		Assert.Null(round.Report);
	}

	[Fact]
	public async Task InterruptAsync_WhenTheWorkerReportsAfterTheRead_RefusesBecauseTheRoundIsNoLongerOpenAndLeavesTheNextRoundRunning()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork work = await rig.ClaimAsync();
		CardPipelineService other = rig.CreateSecondPipeline();
		interceptor.Arm(async () => ResultAssert.SucceededWithOptionalValue(await other.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Work done"), TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(work.RoundId, "The pipeline was stopped.", TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Contains("no longer open", message, StringComparison.Ordinal);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal([RoundKind.Work, RoundKind.Gate], rounds.Select(round => round.Kind));
		Assert.Equal([RoundOutcome.Completed, null], rounds.Select(round => round.Outcome));
		Assert.Equal("Work done", rounds[0].Report);
	}

	[Fact]
	public async Task RecordResultAsync_WhenAnotherWorkerReportsTheSameRoundFirst_ExactlyOneReportWins()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork work = await rig.ClaimAsync();
		CardPipelineService other = rig.CreateSecondPipeline();
		interceptor.Arm(async () => ResultAssert.SucceededWithOptionalValue(await other.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "From the other worker"), TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "From me"), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Contains("no longer open", message, StringComparison.Ordinal);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(2, rounds.Count);
		Assert.Equal("From the other worker", rounds[0].Report);
		Assert.Equal(RoundKind.Gate, rounds[1].Kind);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Gates, stored.Lane);
		Assert.Equal(2, stored.Version);
	}

	[Fact]
	public async Task RecordResultAsync_WhenAPersonTriesToDeleteTheReviewerAfterTheRead_RefusesTheDeleteAndStillOpensTheReview()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Rita", canWork: false, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card", worker.Id, reviewer.Id);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		interceptor.Arm(async () => ResultAssert.Failed(await rig.Agents.DeleteAsync(reviewer.Id, TestContext.Current.CancellationToken)));

		PipelineWork? review = ResultAssert.SucceededWithOptionalValue(await rig.Pipeline.RecordResultAsync(gate.RoundId, RoundResult.Succeeded, new RoundReport(null, "42 tests passed"), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.NotNull(review);
		Assert.Equal(RoundKind.Review, review.Kind);
		Assert.Equal("Rita", review.Agent?.Name);
		Assert.NotNull(await TestData.FindAgentAsync(rig.Database, reviewer.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);
		Assert.Equal(reviewer.Id, stored.ReviewerAgentId);
		Assert.Equal(3, stored.Version);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(3, rounds.Count);
		Assert.Equal(RoundOutcome.Passed, rounds[1].Outcome);
		Assert.Equal("Wanda", rounds[0].Agent?.Name);
	}

	[Fact]
	public async Task RecordResultAsync_WhenAPersonTriesToRemoveTheWorkersRoleAfterTheRead_RefusesTheChangeAndStillOpensTheNextWorkRound()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card", worker.Id, worker.Id);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		interceptor.Arm(async () => ResultAssert.Failed(await rig.Agents.UpdateAsync(worker.Id, new AgentInput("Wanda", HarnessKind.ClaudeCode, "claude", string.Empty, false, true), TestContext.Current.CancellationToken)));

		PipelineWork? work = ResultAssert.SucceededWithOptionalValue(await rig.Pipeline.RecordResultAsync(gate.RoundId, RoundResult.Failed, new RoundReport("Build failed", null), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.NotNull(work);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal("Wanda", work.Agent?.Name);
		Assert.True((await TestData.FindAgentAsync(rig.Database, worker.Id))?.CanWork, "The refused change must not be applied.");
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(worker.Id, stored.WorkerAgentId);
		Assert.Equal(worker.Id, stored.ReviewerAgentId);
		Assert.Equal(CardLane.InProgress, stored.Lane);
	}
}
