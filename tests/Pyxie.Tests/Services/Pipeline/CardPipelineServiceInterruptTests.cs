using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>Giving a round back closes it as interrupted with the reason and leaves the card where it is on the same attempt; the next claim resumes it at the same stage. A round that is already closed cannot be given back, and reporting a result for an interrupted round is refused. When both race for one round, exactly one wins.</summary>
public sealed class CardPipelineServiceInterruptTests
{
	private const string Stopped = "The pipeline was stopped.";
	private const string NoLongerOpen = "This round is no longer open.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task InterruptAsync_ClosesTheRoundAsInterruptedWithTheNoteAndLeavesTheCardInItsLaneOnTheSameAttempt(RoundKind kind)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await PipelineArrangement.ClaimAtAttemptAsync(rig, kind, attempt: 2);
		CardLane lane = PipelineRules.LaneOf(kind);
		await TestData.AddCardAsync(rig.Database, "Behind", lane);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);
		DateTime interruptedAt = rig.Database.Clock.Tick();

		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(running.RoundId, $"  {Stopped}  ", Token));

		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(lane, stored.Lane);
		Assert.Equal(before.SortOrder, stored.SortOrder);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal(before.Version + 1, stored.Version);
		Assert.Equal(interruptedAt, stored.UpdatedUtc);
		Assert.Equal("Card,Behind", await TestData.ReadLaneNamesAsync(rig.Database, lane));
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		CardRound interrupted = rounds[^1];
		Assert.Equal(running.RoundId, interrupted.Id);
		Assert.Equal(kind, interrupted.Kind);
		Assert.Equal(2, interrupted.Attempt);
		Assert.Equal(RoundOutcome.Interrupted, interrupted.Outcome);
		Assert.Equal(Stopped, interrupted.Note);
		Assert.Null(interrupted.Report);
		Assert.Equal(interruptedAt, interrupted.EndedUtc);
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task ClaimNextAsync_AfterAnInterruption_ResumesTheSameKindOnTheSameAttemptWithTheSameAgentInTheSameLane(RoundKind kind)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await PipelineArrangement.ClaimAtAttemptAsync(rig, kind, attempt: 2);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(running.RoundId, Stopped, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(card.Id, resumed.CardId);
		Assert.Equal(kind, resumed.Kind);
		Assert.Equal(2, resumed.Attempt);
		Assert.Equal(running.Sequence + 1, resumed.Sequence);
		Assert.NotEqual(running.RoundId, resumed.RoundId);
		Assert.Equal(running.Agent?.Name, resumed.Agent?.Name);
		Assert.Equal(kind.RunsAgent(), resumed.Briefing is not null);
		Assert.Equal([$"{kind}@2:Interrupted", $"{kind}@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(PipelineRules.LaneOf(kind), stored.Lane);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal(CardState.Ready, stored.State);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAnInterruptedWorkRound_BriefsTheWorkerOnHowThePreviousAttemptEnded()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await rig.CreateCardAsync("Card");
		PipelineWork running = await PipelineArrangement.ClaimAtAttemptAsync(rig, RoundKind.Work, attempt: 2);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(running.RoundId, Stopped, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.NotNull(resumed.Briefing);
		string previous = BriefingSections.Get(resumed.Briefing, "How the previous attempt ended");
		Assert.Contains("Round 3, attempt 2, Work, Interrupted", previous, StringComparison.Ordinal);
		Assert.Contains(Stopped, previous, StringComparison.Ordinal);
		Assert.Contains("- Stage: Work (attempt 2 of 5; round 4 of this card)", resumed.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InterruptAsync_OfAnUnknownRound_IsRefusedAndChangesNothing()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		await rig.ClaimAsync();
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(9999, Stopped, Token));

		Assert.Equal("Round not found.", message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		Assert.Equal(["Work@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task InterruptAsync_OfARoundThatAlreadyEnded_IsRefusedAndKeepsItsOutcomeAndNote()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Succeeded, "Handed off", "The report");
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(work.RoundId, Stopped, Token));

		Assert.Equal(NoLongerOpen, message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		CardRound ended = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[0];
		Assert.Equal(RoundOutcome.Completed, ended.Outcome);
		Assert.Equal("Handed off", ended.Note);
		Assert.Equal("The report", ended.Report);
	}

	[Fact]
	public async Task InterruptAsync_OfARoundThatWasAlreadyInterrupted_IsRefused()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(work.RoundId, Stopped, Token));

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(work.RoundId, "Another reason", Token));

		Assert.Equal(NoLongerOpen, message);
		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal(Stopped, round.Note);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public async Task InterruptAsync_WithABlankNote_IsRefusedAndTheRoundStaysOpen(string note)
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		await rig.ClaimAsync();
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);
		CardRound open = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Single();

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(open.Id, note, Token));

		Assert.Equal("A note is required when a round is interrupted.", message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		Assert.Equal(["Work@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task InterruptAsync_WithANoteThatIsTooLong_IsRefusedAndTheRoundStaysOpen()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.InterruptAsync(work.RoundId, new string('x', PyxieLimits.MaxRoundNoteLength + 1), Token));

		Assert.Equal($"Note must be {PyxieLimits.MaxRoundNoteLength} characters or fewer.", message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		Assert.Equal(["Work@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task InterruptAsync_WithANoteAtTheLimitAfterTrimming_IsAccepted()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		string note = new('x', PyxieLimits.MaxRoundNoteLength);

		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(work.RoundId, $"  {note}  ", Token));

		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal(RoundOutcome.Interrupted, round.Outcome);
		Assert.Equal(note, round.Note);
	}

	[Fact]
	public async Task RecordResultAsync_ForARoundThatWasInterrupted_IsRefusedAndOpensNothing()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(work.RoundId, Stopped, Token));
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Late work"), Token));

		Assert.Equal(NoLongerOpen, message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal(RoundOutcome.Interrupted, round.Outcome);
		Assert.Null(round.Report);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task RecordResultAsyncAndInterruptAsync_RacingForOneRound_LetExactlyOneWin(bool interruptWins)
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await rig.AddGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		CardPipelineService other = rig.CreateSecondPipeline();
		ServiceResult? interrupt = null;
		ServiceResult<PipelineWork?>? record = null;

		// The caller that runs from inside the interceptor commits first; the other one, already past its read, then finds the round closed.
		if (interruptWins)
		{
			interceptor.Arm(async () => interrupt = await other.InterruptAsync(work.RoundId, Stopped, Token));
			record = await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Done"), Token);
		}
		else
		{
			interceptor.Arm(async () => record = await other.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Done"), Token));
			interrupt = await rig.Pipeline.InterruptAsync(work.RoundId, Stopped, Token);
		}

		interceptor.AssertFired();
		Assert.NotNull(interrupt);
		Assert.NotNull(record);
		Assert.Equal(1, new[] { interrupt.Succeeded, record.Succeeded }.Count(succeeded => succeeded));
		Assert.Equal(interruptWins, interrupt.Succeeded);
		Assert.Equal(NoLongerOpen, interruptWins ? record.Error : interrupt.Error);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		if (interruptWins)
		{
			Assert.Equal(["Work@1:Interrupted"], rounds.Select(PipelineRoundLog.Describe));
			Assert.Equal(Stopped, rounds[0].Note);
			Assert.Null(rounds[0].Report);
			Assert.Equal(CardLane.InProgress, stored.Lane);
			Assert.Equal(2, stored.Version);
		}
		else
		{
			Assert.Equal(["Work@1:Completed", "Gate@1:Open"], rounds.Select(PipelineRoundLog.Describe));
			Assert.Equal("Done", rounds[0].Report);
			Assert.Null(rounds[0].Note);
			Assert.Equal(CardLane.Gates, stored.Lane);
			Assert.Equal(2, stored.Version);
		}
	}
}
