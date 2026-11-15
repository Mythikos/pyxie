using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A reviewer is given the worker's latest handoff, the gate results the work passed, and the previous rejection to check the findings were addressed.</summary>
public sealed class CardBriefingBuilderReviewRoundTests
{
	private static string BuildReview(int sequence, params CardRound[] previous)
	{
		return CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Review, sequence, previous, BriefingSections.TargetBranch);
	}

	[Fact]
	public void Build_IncludesTheLatestWorkReportTheLatestPassedGateReportAndThePreviousRejection()
	{
		string briefing = BuildReview(6, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "FIRSTHANDOFF"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "FIRSTGATEOUTPUT"), TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Missing a test", report: "REJECTIONFINDINGS"), TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Walt", report: "SECONDHANDOFF"), TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed, report: "SECONDGATEOUTPUT"));

		Assert.Contains("## Work to review", briefing, StringComparison.Ordinal);
		Assert.Contains("Round 4, attempt 1, Work, Completed by Walt.", briefing, StringComparison.Ordinal);
		Assert.Contains("SECONDHANDOFF", briefing, StringComparison.Ordinal);
		Assert.Contains("## Gate results", briefing, StringComparison.Ordinal);
		Assert.Contains("Round 5, attempt 1, Gates, Passed.", briefing, StringComparison.Ordinal);
		Assert.Contains("SECONDGATEOUTPUT", briefing, StringComparison.Ordinal);
		Assert.Contains("## Previous review findings", briefing, StringComparison.Ordinal);
		Assert.Contains("Round 3, attempt 1, Review, Rejected by Rita.", briefing, StringComparison.Ordinal);
		Assert.Contains("Missing a test", briefing, StringComparison.Ordinal);
		Assert.Contains("REJECTIONFINDINGS", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_LeavesOutTheReportsOfSupersededWorkAndGateRounds()
	{
		string briefing = BuildReview(6, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "FIRSTHANDOFF"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "FIRSTGATEOUTPUT"), TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Missing a test"), TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Walt", report: "SECONDHANDOFF"), TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed, report: "SECONDGATEOUTPUT"));

		Assert.DoesNotContain("FIRSTHANDOFF", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTGATEOUTPUT", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_OfAFirstReview_HasNoPreviousFindings()
	{
		string briefing = BuildReview(3, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "HANDOFF"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "GATEOUTPUT"));

		Assert.Contains("HANDOFF", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Previous review findings", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_OnlyQuotesWorkThatCompletedAndGatesThatPassed()
	{
		string briefing = BuildReview(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Crashed", report: "ERROREDWORK"), TestRound.Create(2, RoundKind.Work, RoundOutcome.Interrupted, "Wanda", report: "INTERRUPTEDWORK"), TestRound.Create(3, RoundKind.Gate, RoundOutcome.Failed, note: "Failed", report: "FAILEDGATE"), TestRound.Create(4, RoundKind.Review, RoundOutcome.Errored, "Rita", note: "Crashed", report: "ERROREDREVIEW"));

		Assert.DoesNotContain("## Work to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Gate results", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Previous review findings", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("ERROREDWORK", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("FAILEDGATE", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("ERROREDREVIEW", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForReview_NeverIncludesTheWorkersSections()
	{
		string briefing = BuildReview(4, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Lint errors", report: "LINTOUTPUT"), TestRound.Create(3, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Timed out"));

		Assert.DoesNotContain("## Why this card came back", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## How the previous attempt ended", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("LINTOUTPUT", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesASkippedGateRoundSoTheReviewerKnowsNoGatesRan()
	{
		string briefing = BuildReview(3, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "HANDOFF"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Skipped, note: "No gates are enabled."));

		string gates = BriefingSections.Get(briefing, "Gate results");
		Assert.Contains("Round 2, attempt 1, Gates, Skipped.", gates, StringComparison.Ordinal);
		Assert.Contains("No gates are enabled.", gates, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAMergeConflictAndRework_ReviewsTheNewWorkAndIgnoresTheConflictReport()
	{
		string briefing = BuildReview(7, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "FIRSTHANDOFF"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "FIRSTGATEOUTPUT"), TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Rita"), TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict in Login.cs", report: "CONFLICTREPORT", attempt: 1), TestRound.Create(5, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "RESOLVEDHANDOFF", attempt: 2), TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed, report: "SECONDGATEOUTPUT", attempt: 2));

		Assert.Contains("RESOLVEDHANDOFF", briefing, StringComparison.Ordinal);
		Assert.Contains("SECONDGATEOUTPUT", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTHANDOFF", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("CONFLICTREPORT", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Previous review findings", briefing, StringComparison.Ordinal);
		Assert.Contains("- Round 4, attempt 1, Merge, Conflicted: Conflict in Login.cs", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ShowsTheNoteOfTheWorkToReviewWhenThereIsOne()
	{
		string briefing = BuildReview(3, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: "Left the cache untouched on purpose"));

		Assert.Contains("Left the cache untouched on purpose", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesALongMultiLineRejectionNoteInFullUnderPreviousReviewFindingsSeparateFromTheClippedHistoryLine()
	{
		string note = BriefingSections.LongMultiLineNote;

		string briefing = BuildReview(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: note), TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Wanda"));

		Assert.True(note.Length >= 500, "The note must be longer than the history line keeps.");
		Assert.Contains(note, BriefingSections.Get(briefing, "Previous review findings"), StringComparison.Ordinal);
		string history = BriefingSections.Get(briefing, "History");
		Assert.DoesNotContain(note, history, StringComparison.Ordinal);
		Assert.DoesNotContain("Finding 8:", history, StringComparison.Ordinal);
	}
}
