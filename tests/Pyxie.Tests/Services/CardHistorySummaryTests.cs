using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CardHistorySummaryTests
{
	private static readonly DateTime s_started = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

	private static RoundSummary Round(int sequence, RoundKind kind, RoundOutcome? outcome, string? agentName = null, string? note = null, int attempt = 1)
	{
		return new RoundSummary(CardId: 1, sequence, kind, attempt, outcome, note, agentName, s_started, EndedUtc: null);
	}

	[Fact]
	public void From_NoRounds_IsTheEmptySummary()
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, []);

		Assert.Same(CardHistorySummary.Empty, summary);
		Assert.Equal(new CardHistorySummary(0, 0, 0, 0, 0, false, null), summary);
	}

	[Fact]
	public void From_ACardInErrorWithNoRounds_SaysThePipelineStoppedIt()
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Error, []);

		Assert.Equal("Stopped by the pipeline.", summary.StopReason);
		Assert.Equal(CardHistorySummary.Empty with { StopReason = "Stopped by the pipeline." }, summary);
	}

	[Fact]
	public void From_CountsEveryKindOfRoundAndHowOftenTheCardWasSentBack()
	{
		List<RoundSummary> rounds =
		[
			Round(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			Round(2, RoundKind.Gate, RoundOutcome.Failed),
			Round(3, RoundKind.Work, RoundOutcome.Completed, "Walt", attempt: 2),
			Round(4, RoundKind.Gate, RoundOutcome.Passed, attempt: 2),
			Round(5, RoundKind.Review, RoundOutcome.Rejected, "Rita", attempt: 2),
			Round(6, RoundKind.Work, RoundOutcome.Completed, "Wilma", attempt: 3),
			Round(7, RoundKind.Gate, RoundOutcome.Passed, attempt: 3),
			Round(8, RoundKind.Review, RoundOutcome.Approved, "Ray", attempt: 3),
			Round(9, RoundKind.Merge, RoundOutcome.Merged, attempt: 3),
		];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds);

		Assert.Equal(new CardHistorySummary(3, 3, 2, 0, 2, false, null), summary);
	}

	[Fact]
	public void From_CountsResolveRoundsAndNamesTheMergeAgentApartFromTheWorker()
	{
		List<RoundSummary> rounds =
		[
			Round(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			Round(2, RoundKind.Gate, RoundOutcome.Passed),
			Round(3, RoundKind.Review, RoundOutcome.Approved, "Ray"),
			Round(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Merging main into pyxie/card-1 conflicts in 1 file."),
			Round(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", attempt: 2),
			Round(6, RoundKind.Gate, RoundOutcome.Passed, attempt: 2),
			Round(7, RoundKind.Review, RoundOutcome.Approved, "Ray", attempt: 2),
			Round(8, RoundKind.Merge, RoundOutcome.Merged, attempt: 2),
		];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds);

		Assert.Equal(new CardHistorySummary(1, 2, 2, 1, 1, false, null), summary);
	}

	[Fact]
	public void From_ReadsTheRoundsInSequenceOrderWhateverOrderTheyArriveIn()
	{
		List<RoundSummary> rounds =
		[
			Round(3, RoundKind.Work, RoundOutcome.Blocked, "Wanda", note: "Latest round"),
			Round(1, RoundKind.Work, RoundOutcome.Blocked, "Wanda", note: "First round"),
			Round(2, RoundKind.Gate, RoundOutcome.Failed),
		];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Error, rounds);

		Assert.Equal("Latest round", summary.StopReason);
		Assert.False(summary.IsRunning, "No round is open.");
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed, 1)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected, 1)]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted, 1)]
	[InlineData(RoundKind.Work, RoundOutcome.Errored, 0)]
	[InlineData(RoundKind.Gate, RoundOutcome.Errored, 0)]
	[InlineData(RoundKind.Merge, RoundOutcome.Outdated, 0)]
	[InlineData(RoundKind.Review, RoundOutcome.Blocked, 0)]
	[InlineData(RoundKind.Work, RoundOutcome.Interrupted, 0)]
	[InlineData(RoundKind.Gate, RoundOutcome.Skipped, 0)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Resolved, 0)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Errored, 0)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Blocked, 0)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Interrupted, 0)]
	public void From_OnlyFailedGatesRejectionsAndMergeConflictsCountAsSendBacks(RoundKind kind, RoundOutcome outcome, int expected)
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, [Round(1, kind, outcome)]);

		Assert.Equal(expected, summary.SendBacks);
	}

	[Fact]
	public void From_DoesNotCountSkippedRoundsAsGateOrReviewRounds()
	{
		List<RoundSummary> rounds =
		[
			Round(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			Round(2, RoundKind.Gate, RoundOutcome.Skipped, note: "No gates are enabled."),
			Round(3, RoundKind.Review, RoundOutcome.Skipped, note: "No reviewer is assigned."),
			Round(4, RoundKind.Merge, RoundOutcome.Merged),
		];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds);

		Assert.Equal(1, summary.WorkRounds);
		Assert.Equal(0, summary.GateRounds);
		Assert.Equal(0, summary.ReviewRounds);
		Assert.Equal(0, summary.SendBacks);
	}

	[Fact]
	public void From_CountsAGateAndAReviewThatRanAlongsideSkippedOnes()
	{
		List<RoundSummary> rounds =
		[
			Round(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			Round(2, RoundKind.Gate, RoundOutcome.Skipped),
			Round(3, RoundKind.Review, RoundOutcome.Approved, "Rita"),
		];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds);

		Assert.Equal(0, summary.GateRounds);
		Assert.Equal(1, summary.ReviewRounds);
	}

	[Fact]
	public void From_ARoundWithoutAnOutcomeIsRunning()
	{
		List<RoundSummary> rounds =
		[
			Round(1, RoundKind.Work, RoundOutcome.Errored, "Wanda", attempt: 1),
			Round(2, RoundKind.Work, outcome: null, "Wanda", attempt: 2),
		];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds);

		Assert.True(summary.IsRunning, "The second round is open.");
		Assert.Equal(2, summary.WorkRounds);
	}

	[Fact]
	public void From_ACardThatIsNotInErrorHasNoStopReasonWhateverItsLastRoundWas()
	{
		List<RoundSummary> rounds = [Round(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), Round(2, RoundKind.Gate, RoundOutcome.Blocked, note: "Gate command not found")];

		Assert.Null(CardHistorySummary.From(CardState.Ready, rounds).StopReason);
	}

	[Fact]
	public void From_ABlockedRoundStopsTheCardWithItsNoteAsTheReason()
	{
		List<RoundSummary> rounds = [Round(1, RoundKind.Work, RoundOutcome.Blocked, "Wanda", note: "The task does not say which login page.")];

		CardHistorySummary summary = CardHistorySummary.From(CardState.Error, rounds);

		Assert.Equal("The task does not say which login page.", summary.StopReason);
	}

	[Fact]
	public void From_ABlockedRoundWithoutANoteFallsBackToTheGenericReason()
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Error, [Round(1, RoundKind.Work, RoundOutcome.Blocked, "Wanda")]);

		Assert.Equal("Stopped by the pipeline.", summary.StopReason);
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed, 3, "Stopped after 3 attempts. Gates Failed: Build failed")]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected, 3, "Stopped after 3 attempts. Review Rejected: Build failed")]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted, 2, "Stopped after 2 attempts. Merge Conflicted: Build failed")]
	[InlineData(RoundKind.Work, RoundOutcome.Errored, 5, "Stopped after 5 attempts. Work Errored: Build failed")]
	[InlineData(RoundKind.Resolve, RoundOutcome.Errored, 4, "Stopped after 4 attempts. Resolve Errored: Build failed")]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed, 1, "Stopped after 1 attempt. Gates Failed: Build failed")]
	public void From_ACardThatRanOutOfAttemptsSaysHowManyItUsedAndWhatTheLastSetbackWas(RoundKind kind, RoundOutcome outcome, int attempt, string expected)
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Error, [Round(1, kind, outcome, note: "Build failed", attempt: attempt)]);

		Assert.Equal(expected, summary.StopReason);
	}

	[Fact]
	public void From_ACardThatRanOutOfAttemptsWithoutANoteLeavesTheNoteOutOfTheReason()
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Error, [Round(1, RoundKind.Gate, RoundOutcome.Failed, attempt: 3)]);

		Assert.Equal("Stopped after 3 attempts. Gates Failed.", summary.StopReason);
	}

	[Theory]
	[InlineData(RoundOutcome.Completed)]
	[InlineData(RoundOutcome.Passed)]
	[InlineData(RoundOutcome.Merged)]
	[InlineData(RoundOutcome.Outdated)]
	[InlineData(RoundOutcome.Resolved)]
	[InlineData(RoundOutcome.Skipped)]
	[InlineData(RoundOutcome.Interrupted)]
	public void From_ACardInErrorWhoseLastRoundIsNeitherASetbackNorBlockedFallsBackToTheGenericReason(RoundOutcome lastOutcome)
	{
		List<RoundSummary> rounds = [Round(1, RoundKind.Gate, RoundOutcome.Failed, note: "Earlier failure"), Round(2, RoundKind.Work, lastOutcome, "Wanda", note: "Some note")];

		Assert.Equal("Stopped by the pipeline.", CardHistorySummary.From(CardState.Error, rounds).StopReason);
	}

	[Fact]
	public void From_TheStopReasonComesFromTheLastRoundInSequenceOrder()
	{
		List<RoundSummary> rounds =
		[
			Round(2, RoundKind.Gate, RoundOutcome.Failed, note: "Last failure", attempt: 3),
			Round(1, RoundKind.Work, RoundOutcome.Blocked, "Wanda", note: "First round only"),
		];

		Assert.Equal("Stopped after 3 attempts. Gates Failed: Last failure", CardHistorySummary.From(CardState.Error, rounds).StopReason);
	}

	[Fact]
	public void RoundSummaryFrom_CopiesTheFieldsOfAStoredRoundAndTheRecordedAgentName()
	{
		DateTime ended = s_started.AddMinutes(7);
		CardRound round = TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Missing a test", attempt: 2, startedUtc: s_started, endedUtc: ended);
		round.CardId = 9;

		RoundSummary summary = RoundSummary.From(round);

		Assert.Equal(new RoundSummary(CardId: 9, Sequence: 3, RoundKind.Review, Attempt: 2, RoundOutcome.Rejected, "Missing a test", "Rita", s_started, ended), summary);
	}

	[Fact]
	public void RoundSummaryFrom_KeepsARunningRoundWithoutAnOutcomeNoteOrAgent()
	{
		RoundSummary summary = RoundSummary.From(TestRound.Create(1, RoundKind.Gate, outcome: null));

		Assert.Null(summary.Outcome);
		Assert.Null(summary.Note);
		Assert.Null(summary.AgentName);
	}
}
