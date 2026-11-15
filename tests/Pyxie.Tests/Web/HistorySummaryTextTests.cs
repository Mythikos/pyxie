using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class HistorySummaryTextTests
{
	[Fact]
	public void Of_CountsWorkGateAndReviewRoundsAndSendBacks()
	{
		CardHistorySummary summary = CardHistorySummary.From(
			CardState.Ready,
			[
				RoundSummary.From(TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Claude")),
				RoundSummary.From(TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Tests failed.")),
				RoundSummary.From(TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Claude", attempt: 2)),
				RoundSummary.From(TestRound.Create(4, RoundKind.Gate, RoundOutcome.Passed, attempt: 2)),
				RoundSummary.From(TestRound.Create(5, RoundKind.Review, RoundOutcome.Approved, "Codex", attempt: 2)),
			]);

		Assert.Equal("2 work, 2 gate, 1 review, 1 sent back", HistorySummaryText.Of(summary));
	}

	[Fact]
	public void Of_CountsResolveRoundsOnACardThatHadOne()
	{
		CardHistorySummary summary = CardHistorySummary.From(
			CardState.Ready,
			[
				RoundSummary.From(TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Claude")),
				RoundSummary.From(TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed)),
				RoundSummary.From(TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Codex")),
				RoundSummary.From(TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Merging main into pyxie/card-1 conflicts in 1 file.")),
				RoundSummary.From(TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", attempt: 2)),
				RoundSummary.From(TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed, attempt: 2)),
				RoundSummary.From(TestRound.Create(7, RoundKind.Review, RoundOutcome.Approved, "Codex", attempt: 2)),
			]);

		Assert.Equal("1 work, 2 gate, 2 review, 1 resolve, 1 sent back", HistorySummaryText.Of(summary));
	}

	[Fact]
	public void Of_SaysRunningWhileARoundIsInProgress()
	{
		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, [RoundSummary.From(TestRound.Create(1, RoundKind.Work, outcome: null, agentName: "Claude"))]);

		Assert.Equal("1 work, 0 gate, 0 review, 0 sent back, running", HistorySummaryText.Of(summary));
	}

	[Fact]
	public void Of_DoesNotRepeatTheStopReasonTheDialogShowsInItsAlert()
	{
		CardHistorySummary summary = CardHistorySummary.From(
			CardState.Error,
			[RoundSummary.From(TestRound.Create(1, RoundKind.Work, RoundOutcome.Blocked, "Claude", note: "The task is unclear."))]);

		Assert.NotNull(summary.StopReason);
		Assert.DoesNotContain("unclear", HistorySummaryText.Of(summary), StringComparison.Ordinal);
	}
}
