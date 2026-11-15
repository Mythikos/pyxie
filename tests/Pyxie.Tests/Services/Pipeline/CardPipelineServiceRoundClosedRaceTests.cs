using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A round another caller closes while the service is working on it is never overwritten. The service reads the card's version before it checks that the round is open, so a version that still matches at the compare-and-set proves the round was open when it was read, and it checks the loaded round again once the compare-and-set has matched. A round closed behind the service's back without a version bump is caught by that second check alone.</summary>
public sealed class CardPipelineServiceRoundClosedRaceTests
{
	private const string NotOpen = "This round is no longer open.";
	private const string StoppedNote = "The pipeline was stopped.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string Describe(IEnumerable<CardRound> rounds)
	{
		return string.Join(", ", rounds.Select(round => $"{round.Kind}:{round.Outcome?.ToString() ?? "Open"}:{round.Note}:{round.Report}"));
	}

	[Fact]
	public async Task InterruptAsync_RefusesARoundThatTheWorkerRecordedWhileTheCardWasBeingRead_AndLeavesTheRecordedResult()
	{
		ConcurrentReadInterceptor interceptor = new("FROM \"Cards\"");
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		CardPipelineService worker = rig.CreateSecondPipeline();
		interceptor.Arm(async () => ResultAssert.SucceededWithOptionalValue(await worker.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Work done"), Token)));

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(work.RoundId, StoppedNote, Token));

		interceptor.AssertFired();
		Assert.Equal(NotOpen, message);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.True(rounds is [{ Kind: RoundKind.Work, Outcome: RoundOutcome.Completed, Note: null, Report: "Work done" }, { Kind: RoundKind.Gate, Outcome: null }], $"The recorded work round must stay completed and the gate round open, but the history is [{Describe(rounds)}].");
		Assert.Equal(CardLane.Gates, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesARoundThatWasInterruptedWhileTheCardWasBeingRead_AndLeavesTheInterruption()
	{
		ConcurrentReadInterceptor interceptor = new("FROM \"Cards\"");
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		CardPipelineService other = rig.CreateSecondPipeline();
		interceptor.Arm(async () => ResultAssert.Succeeded(await other.InterruptAsync(work.RoundId, StoppedNote, Token)));

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Late work"), Token));

		interceptor.AssertFired();
		Assert.Equal(NotOpen, message);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.True(rounds is [{ Kind: RoundKind.Work, Outcome: RoundOutcome.Interrupted, Note: StoppedNote, Report: null }], $"The interrupted round must stay interrupted and no next round may open, but the history is [{Describe(rounds)}].");
		Assert.Equal(CardLane.InProgress, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
	}

	[Fact]
	public async Task InterruptAsync_RefusesARoundThatWasClosedWithoutTheCardVersionChanging_AndLeavesItAsItWas()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		WorkCard claimed = (await TestData.FindCardAsync(rig.Database, card.Id)) ?? throw new InvalidOperationException("The card exists.");
		interceptor.Arm($"UPDATE \"Rounds\" SET \"Outcome\" = {(int)RoundOutcome.Completed}, \"EndedUtc\" = '2030-01-01 00:00:00', \"Note\" = 'Closed elsewhere' WHERE \"Id\" = {work.RoundId}");

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(work.RoundId, StoppedNote, Token));

		interceptor.AssertFired();
		Assert.Equal(NotOpen, message);
		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal((RoundOutcome.Completed, "Closed elsewhere"), (round.Outcome, round.Note));
		Assert.Equal(claimed.Version, (await TestData.FindCardAsync(rig.Database, card.Id))?.Version);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesARoundThatWasClosedWithoutTheCardVersionChanging_AndLeavesItAsItWas()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		WorkCard claimed = (await TestData.FindCardAsync(rig.Database, card.Id)) ?? throw new InvalidOperationException("The card exists.");
		interceptor.Arm($"UPDATE \"Rounds\" SET \"Outcome\" = {(int)RoundOutcome.Interrupted}, \"EndedUtc\" = '2030-01-01 00:00:00', \"Note\" = 'Closed elsewhere' WHERE \"Id\" = {work.RoundId}");

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Late work"), Token));

		interceptor.AssertFired();
		Assert.Equal(NotOpen, message);
		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal((RoundOutcome.Interrupted, "Closed elsewhere", (string?)null), (round.Outcome, round.Note, round.Report));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal((CardLane.InProgress, claimed.Version), (stored.Lane, stored.Version));
	}
}
