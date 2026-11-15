using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A worker is told why the card came back, in full, and how its last attempt ended when that attempt did not finish.</summary>
public sealed class CardBriefingBuilderWorkRoundTests
{
	private static string BuildWork(int sequence, params CardRound[] previous)
	{
		return CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence, previous, BriefingSections.TargetBranch);
	}

	[Fact]
	public void Build_AfterAGateFailure_QuotesTheFailureNoteAndReportInFull()
	{
		string gateOutput = string.Concat(Enumerable.Repeat("FAIL tests/Login.cs line\n", 400));

		string briefing = BuildWork(3, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "Implemented the fix"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "2 tests failed", report: gateOutput));

		Assert.Contains("## Why this card came back", briefing, StringComparison.Ordinal);
		Assert.Contains("Round 2, attempt 1, Gates, Failed.", briefing, StringComparison.Ordinal);
		Assert.Contains("2 tests failed", briefing, StringComparison.Ordinal);
		Assert.Contains(gateOutput, briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterARejection_QuotesTheRejectionNoteAndReportInFull()
	{
		string findings = string.Concat(Enumerable.Repeat("The password check ignores the plus sign.\n", 100));

		string briefing = BuildWork(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "The fix misses a case", report: findings), TestRound.Create(4, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Harness crashed"));

		Assert.Contains("## Why this card came back", briefing, StringComparison.Ordinal);
		Assert.Contains("Round 3, attempt 1, Review, Rejected by Rita.", briefing, StringComparison.Ordinal);
		Assert.Contains("The fix misses a case", briefing, StringComparison.Ordinal);
		Assert.Contains(findings, briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesOnlyTheLatestReasonTheCardCameBack()
	{
		string briefing = BuildWork(6, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "old gate failure", report: "OLDGATEOUTPUT"), TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(4, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(5, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "new rejection", report: "NEWFINDINGS"));

		Assert.Contains("NEWFINDINGS", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("OLDGATEOUTPUT", briefing, StringComparison.Ordinal);
		Assert.Contains("- Round 2, attempt 1, Gates, Failed: old gate failure", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesTheLatestReasonEvenWhenALaterGatePassedAndTheCardWasWorkedAgain()
	{
		string briefing = BuildWork(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Handle empty input"), TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Wanda"));

		Assert.Contains("## Why this card came back", briefing, StringComparison.Ordinal);
		Assert.Contains("Handle empty input", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WithoutASendBack_HasNoReasonSection()
	{
		string briefing = BuildWork(
			3,
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed));

		Assert.DoesNotContain("## Why this card came back", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundOutcome.Errored, "harness crashed")]
	[InlineData(RoundOutcome.Blocked, "Needs the staging credentials.")]
	[InlineData(RoundOutcome.Interrupted, "Pyxie stopped while this round was running.")]
	public void Build_AfterAnUnfinishedWorkAttempt_ShowsHowItEnded(RoundOutcome outcome, string note)
	{
		string briefing = BuildWork(2, TestRound.Create(1, RoundKind.Work, outcome, "Wanda", note: note, report: "partial output so far"));

		Assert.Contains("## How the previous attempt ended", briefing, StringComparison.Ordinal);
		Assert.Contains($"Round 1, attempt 1, Work, {outcome}", briefing, StringComparison.Ordinal);
		Assert.Contains("by Wanda.", briefing, StringComparison.Ordinal);
		Assert.Contains(note, briefing, StringComparison.Ordinal);
		Assert.Contains("partial output so far", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ShowsBothWhyTheCardCameBackAndHowTheLastAttemptEnded()
	{
		string briefing = BuildWork(4, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Lint errors", report: "LINTOUTPUT"), TestRound.Create(3, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Timed out"));

		Assert.Contains("## Why this card came back", briefing, StringComparison.Ordinal);
		Assert.Contains("LINTOUTPUT", briefing, StringComparison.Ordinal);
		Assert.Contains("## How the previous attempt ended", briefing, StringComparison.Ordinal);
		Assert.Contains("Timed out", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAWorkAttemptThatCompleted_HasNoEndingSection()
	{
		string briefing = BuildWork(2, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: "Some note"));

		Assert.DoesNotContain("## How the previous attempt ended", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAMergeConflict_QuotesTheConflictNoteAndTheConflictingFilesInFull()
	{
		string files = string.Concat(Enumerable.Range(1, 30).Select(file => $"CONFLICT (content): Merge conflict in src/File{file}.cs\n"));

		string briefing = BuildWork(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Rita"), TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Merging main conflicted in 30 files", report: files));

		string reason = BriefingSections.Get(briefing, "Why this card came back");
		Assert.Contains("Round 4, attempt 1, Merge, Conflicted.", reason, StringComparison.Ordinal);
		Assert.Contains("Merging main conflicted in 30 files", reason, StringComparison.Ordinal);
		Assert.Contains(files, reason, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAMergeConflict_TellsTheWorkerToMergeTheTargetBranchResolveItAndFinishTheMergeRightUnderTheConflict()
	{
		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 5, [TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Rita"), TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Merging release/2 conflicted in src/Login.cs", report: "CONFLICTREPORT")], "release/2");

		string reason = BriefingSections.Get(briefing, "Why this card came back");
		Assert.Contains("CONFLICTREPORT", reason, StringComparison.Ordinal);
		Assert.Contains("Merge `release/2` into this branch with `git merge release/2`", reason, StringComparison.Ordinal);
		Assert.Contains("resolve the conflicts so the card's change and the changes on `release/2` both still hold", reason, StringComparison.Ordinal);
		Assert.Contains("finish the merge; Pyxie commits it with the rest of your work", reason, StringComparison.Ordinal);
		Assert.True(reason.IndexOf("CONFLICTREPORT", StringComparison.Ordinal) < reason.IndexOf("git merge release/2", StringComparison.Ordinal), "The instruction follows what the merge reported.");
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	public void Build_AfterAGateFailureOrARejection_HasNoMergeInstruction(RoundKind kind, RoundOutcome outcome)
	{
		string briefing = BuildWork(3, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, kind, outcome, kind == RoundKind.Review ? "Rita" : null, note: "Needs another go", report: "FINDINGS"));

		Assert.Contains("FINDINGS", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("finish the merge", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAGateFailureThatFollowedAnEarlierConflict_QuotesOnlyTheGateFailureAndHasNoMergeInstruction()
	{
		string briefing = BuildWork(7, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Merge, RoundOutcome.Conflicted, note: "OLDCONFLICT"), TestRound.Create(3, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", report: "THERESOLUTION", attempt: 2), TestRound.Create(4, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed after the merge", report: "GATEFAILURE", attempt: 2));

		string reason = BriefingSections.Get(briefing, "Why this card came back");
		Assert.Contains("GATEFAILURE", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("OLDCONFLICT", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAConflictThatAMergeAgentFailedToResolveAndWasRemoved_StillTellsTheWorkerToMergeAndDoesNotQuoteTheMergeAgentsAttempt()
	{
		string briefing = BuildWork(6, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved, "Rita"), TestRound.Create(3, RoundKind.Merge, RoundOutcome.Conflicted, note: "THECONFLICT", report: "CONFLICTREPORT"), TestRound.Create(4, RoundKind.Resolve, RoundOutcome.Blocked, "Merger", note: "Needs a person", report: "MERGERPARTIAL", attempt: 2));

		string reason = BriefingSections.Get(briefing, "Why this card came back");
		Assert.Contains("Round 3, attempt 1, Merge, Conflicted.", reason, StringComparison.Ordinal);
		Assert.Contains("CONFLICTREPORT", reason, StringComparison.Ordinal);
		Assert.Contains("git merge main", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("## How the previous attempt ended", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("MERGERPARTIAL", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Work, RoundOutcome.Completed)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Resolved)]
	public void Build_WhenARoundThatChangesTheWorkFollowedTheConflict_QuotesItStillButNoLongerAsksForTheMerge(RoundKind kind, RoundOutcome outcome)
	{
		string briefing = BuildWork(7, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Merge, RoundOutcome.Conflicted, note: "THECONFLICT", report: "CONFLICTREPORT"), TestRound.Create(3, kind, outcome, "Walt", attempt: 2), TestRound.Create(4, RoundKind.Gate, RoundOutcome.Passed, attempt: 2), TestRound.Create(5, RoundKind.Review, RoundOutcome.Approved, "Rita", attempt: 2), TestRound.Create(6, RoundKind.Merge, RoundOutcome.Blocked, note: "The target branch has local changes in the way.", attempt: 2));

		string reason = BriefingSections.Get(briefing, "Why this card came back");
		Assert.Contains("CONFLICTREPORT", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WhenARoundThatDidNotChangeTheWorkFollowedTheConflict_StillAsksForTheMerge()
	{
		string briefing = BuildWork(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Merge, RoundOutcome.Conflicted, note: "THECONFLICT", report: "CONFLICTREPORT"), TestRound.Create(3, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Crashed", attempt: 2), TestRound.Create(4, RoundKind.Resolve, RoundOutcome.Blocked, "Merger", note: "Needs a person", attempt: 2));

		Assert.Contains("`git merge main`", BriefingSections.Get(briefing, "Why this card came back"), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForASecondConflictAfterAnEarlierOneWasMerged_AsksForTheMergeAgain()
	{
		string briefing = BuildWork(7, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Merge, RoundOutcome.Conflicted, note: "FIRSTCONFLICT", report: "FIRSTREPORT"), TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Wanda", attempt: 2), TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "SECONDCONFLICT", report: "SECONDREPORT", attempt: 2));

		string reason = BriefingSections.Get(briefing, "Why this card came back");
		Assert.Contains("SECONDREPORT", reason, StringComparison.Ordinal);
		Assert.Contains("`git merge main`", reason, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForWork_KeepsTheMergeInstructionOutOfTheWorkspace()
	{
		string briefing = BuildWork(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict", report: "CONFLICTREPORT"));

		Assert.DoesNotContain("git merge", BriefingSections.Get(briefing, "Workspace"), StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", BriefingSections.Get(briefing, "When you finish"), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAnOutdatedMergeAndNoSendBack_HasNoReasonSection()
	{
		string briefing = BuildWork(5, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved, "Rita"), TestRound.Create(3, RoundKind.Merge, RoundOutcome.Outdated, note: "main moved on"), TestRound.Create(4, RoundKind.Gate, RoundOutcome.Passed));

		Assert.DoesNotContain("## Why this card came back", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WhenTheLastRoundIsNotAWorkRound_HasNoEndingSection()
	{
		string briefing = BuildWork(
			3,
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Errored, note: "Gate runner crashed"));

		Assert.DoesNotContain("## How the previous attempt ended", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WhenAnEarlierWorkAttemptErroredButALaterRoundRan_HasNoEndingSection()
	{
		string briefing = BuildWork(4, TestRound.Create(1, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Crashed"), TestRound.Create(2, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(3, RoundKind.Gate, RoundOutcome.Passed));

		Assert.DoesNotContain("## How the previous attempt ended", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForWork_NeverIncludesTheReviewersSections()
	{
		string briefing = BuildWork(3, TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "WORKHANDOFF"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Failed", report: "GATEOUTPUT"));

		Assert.DoesNotContain("## Work to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Gate results", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Previous review findings", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("WORKHANDOFF", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted)]
	public void Build_QuotesALongMultiLineNoteInFullUnderWhyTheCardCameBackSeparateFromTheClippedHistoryLine(RoundKind kind, RoundOutcome outcome)
	{
		string note = BriefingSections.LongMultiLineNote;

		string briefing = BuildWork(
			3,
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			TestRound.Create(2, kind, outcome, kind == RoundKind.Review ? "Rita" : null, note: note));

		Assert.True(note.Length >= 500, "The note must be longer than the history line keeps.");
		Assert.Contains(note, BriefingSections.Get(briefing, "Why this card came back"), StringComparison.Ordinal);
		string history = BriefingSections.Get(briefing, "History");
		Assert.DoesNotContain(note, history, StringComparison.Ordinal);
		Assert.Contains("Finding 1:", history, StringComparison.Ordinal);
		Assert.DoesNotContain("Finding 8:", history, StringComparison.Ordinal);
	}
}
