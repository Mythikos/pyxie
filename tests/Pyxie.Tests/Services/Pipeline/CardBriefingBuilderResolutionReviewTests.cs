using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>When the latest change to a card is a merge agent's resolution, the reviewer is told it is reviewing a merge, shown the resolution (which the runtime fills with the remerge diff) before the card's own work, and still given the gate results and any earlier rejection. When the latest change is the worker's, the briefing is the one it always was.</summary>
public sealed class CardBriefingBuilderResolutionReviewTests
{
	private const string Target = "release/2";

	private static CardRound[] ResolvedHistory(string resolutionNote = "Combined both versions of the login check", string resolutionReport = "RESOLUTIONREPORT")
	{
		return
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: "Fixed the login", report: "WORKHANDOFF"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "FIRSTGATEOUTPUT"),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Rita", note: "Looks right", report: "FIRSTREVIEW"),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Merging release/2 conflicted in src/Login.cs", report: "CONFLICTREPORT"),
			TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", note: resolutionNote, report: resolutionReport, attempt: 2),
			TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed, report: "SECONDGATEOUTPUT", attempt: 2),
		];
	}

	private static string BuildReview(int attempt = 2, params CardRound[] previous)
	{
		return CardBriefingBuilder.Build(InMemoryCard.Create(attempt: attempt), RoundKind.Review, sequence: previous.Length + 1, previous, Target);
	}

	[Fact]
	public void Build_AfterAResolution_IntroducesAMergeToReviewAndNotWorkToReview()
	{
		string briefing = BuildReview(2, ResolvedHistory());

		string intro = BriefingSections.Get(briefing, "Merge to review");
		Assert.DoesNotContain("## Work to review", briefing, StringComparison.Ordinal);
		Assert.Contains("After the card's work was approved, `release/2` moved on and its changes conflicted with the card's.", intro, StringComparison.Ordinal);
		Assert.Contains("a merge commit, the last commit of this branch", intro, StringComparison.Ordinal);
		Assert.Contains("`git show --remerge-diff HEAD`", intro, StringComparison.Ordinal);
		Assert.Contains("the card's change and the changes on `release/2` both still hold", intro, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAResolution_QuotesTheResolutionWithItsNoteAndReportInFull()
	{
		string diff = string.Concat(Enumerable.Range(1, 200).Select(line => $"+resolved line {line}\n"));

		string briefing = BuildReview(2, ResolvedHistory(resolutionReport: $"How each conflict was resolved.\n\n```diff\n{diff}```"));

		Assert.Contains("## The resolution", briefing, StringComparison.Ordinal);
		string quoted = BriefingSections.Get(briefing, "The resolution");
		Assert.Contains("Round 5, attempt 2, Resolve, Resolved by Merger.", quoted, StringComparison.Ordinal);
		Assert.Contains("Combined both versions of the login check", quoted, StringComparison.Ordinal);
		Assert.Contains(diff, quoted, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAResolution_AlsoQuotesTheCardsWorkAfterTheResolutionAndTheLatestGateResults()
	{
		string briefing = BuildReview(2, ResolvedHistory());

		string work = BriefingSections.Get(briefing, "The card's work");
		Assert.Contains("Round 1, attempt 1, Work, Completed by Wanda.", work, StringComparison.Ordinal);
		Assert.Contains("WORKHANDOFF", work, StringComparison.Ordinal);
		string gates = BriefingSections.Get(briefing, "Gate results");
		Assert.Contains("Round 6, attempt 2, Gates, Passed.", gates, StringComparison.Ordinal);
		Assert.Contains("SECONDGATEOUTPUT", gates, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTGATEOUTPUT", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAResolution_DoesNotQuoteTheConflictItself()
	{
		string briefing = BuildReview(2, ResolvedHistory());

		Assert.DoesNotContain("CONFLICTREPORT", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Why the merge stopped", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Why this card came back", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAResolution_PutsTheMergeThenTheResolutionThenTheWorkThenTheGatesThenAnEarlierRejectionThenTheHistory()
	{
		CardRound[] history =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "WORKHANDOFF"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Missing a test", report: "REJECTIONFINDINGS"),
			TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "SECONDHANDOFF", attempt: 2),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed, attempt: 2),
			TestRound.Create(6, RoundKind.Review, RoundOutcome.Approved, "Rita", attempt: 2),
			TestRound.Create(7, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict", attempt: 2),
			TestRound.Create(8, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", report: "RESOLUTIONREPORT", attempt: 3),
			TestRound.Create(9, RoundKind.Gate, RoundOutcome.Passed, report: "LASTGATEOUTPUT", attempt: 3),
		];

		string briefing = BuildReview(3, history);

		AssertInOrder(briefing, "# Card 7", "## Task", "## Workspace", "## When you finish", "## Merge to review", "## The resolution", "## The card's work", "## Gate results", "## Previous review findings", "## History");
		Assert.Contains("SECONDHANDOFF", BriefingSections.Get(briefing, "The card's work"), StringComparison.Ordinal);
		Assert.DoesNotContain("WORKHANDOFF", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAResolution_KeepsTheReviewersWorkspaceAndResultWords()
	{
		string briefing = BuildReview(2, ResolvedHistory());

		string workspace = BriefingSections.Get(briefing, "Workspace");
		Assert.Contains("git diff release/2...HEAD", workspace, StringComparison.Ordinal);
		Assert.Contains("Do not change files or push", workspace, StringComparison.Ordinal);
		string finish = BriefingSections.Get(briefing, "When you finish");
		Assert.Contains("\"outcome\": \"approved\"", finish, StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.Rejected}`", finish, StringComparison.Ordinal);
		Assert.DoesNotContain($"`{PipelineContract.Resolved}`", finish, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterASecondResolution_QuotesOnlyTheLatestResolution()
	{
		CardRound[] history =
		[
			.. ResolvedHistory(resolutionReport: "FIRSTRESOLUTION"),
			TestRound.Create(7, RoundKind.Review, RoundOutcome.Approved, "Rita", attempt: 2),
			TestRound.Create(8, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict again", attempt: 2),
			TestRound.Create(9, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", report: "SECONDRESOLUTION", attempt: 3),
			TestRound.Create(10, RoundKind.Gate, RoundOutcome.Passed, attempt: 3),
		];

		string briefing = BuildReview(3, history);

		string quoted = BriefingSections.Get(briefing, "The resolution");
		Assert.Contains("Round 9, attempt 3, Resolve, Resolved by Merger.", quoted, StringComparison.Ordinal);
		Assert.Contains("SECONDRESOLUTION", quoted, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTRESOLUTION", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WhenTheWorkerChangedTheCardAfterTheResolution_IsAWorkReviewAgainBecauseTheLatestChangeIsTheWorkers()
	{
		CardRound[] history =
		[
			.. ResolvedHistory(),
			TestRound.Create(7, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed after the merge", report: "GATEFAILURE", attempt: 2),
			TestRound.Create(8, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "REWORKHANDOFF", attempt: 3),
			TestRound.Create(9, RoundKind.Gate, RoundOutcome.Passed, report: "THIRDGATEOUTPUT", attempt: 3),
		];

		string briefing = BuildReview(3, history);

		Assert.Contains("## Work to review", briefing, StringComparison.Ordinal);
		Assert.Contains("REWORKHANDOFF", BriefingSections.Get(briefing, "Work to review"), StringComparison.Ordinal);
		Assert.DoesNotContain("## Merge to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## The resolution", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("RESOLUTIONREPORT", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundOutcome.Errored)]
	[InlineData(RoundOutcome.Blocked)]
	[InlineData(RoundOutcome.Interrupted)]
	public void Build_ForAResolveRoundThatDidNotResolve_ReviewsTheWorkAsItWas(RoundOutcome outcome)
	{
		CardRound[] history =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "WORKHANDOFF"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(3, RoundKind.Resolve, outcome, "Merger", note: "Did not finish", report: "PARTIALRESOLUTION", attempt: 2),
		];

		string briefing = BuildReview(2, history);

		Assert.Contains("## Work to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Merge to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("PARTIALRESOLUTION", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WithoutAResolution_IsTheBriefingItAlwaysWas()
	{
		CardRound[] history =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "WORKHANDOFF"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "GATEOUTPUT"),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Rita"),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Outdated, note: "release/2 moved on"),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed, report: "SECONDGATEOUTPUT"),
		];

		string briefing = BuildReview(1, history);

		AssertInOrder(briefing, "## Workspace", "## When you finish", "## Work to review", "## Gate results", "## History");
		Assert.DoesNotContain("## Merge to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## The resolution", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("remerge", briefing, StringComparison.Ordinal);
		Assert.Contains("WORKHANDOFF", BriefingSections.Get(briefing, "Work to review"), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForAReviewOfAResolutionWithTheLargestContextTheLimitsAllow_KeepsEveryQuotedReportAtItsStartAndEndAndLeavesTheCutToHistory()
	{
		string task = new('d', PyxieLimits.MaxDescriptionLength);
		CardRound[] history =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("W-START", "W-END")),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("G-START", "G-END")),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("R-START", "R-END")),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("C-START", "C-END")),
			TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("X-START", "X-END"), attempt: 2),
			TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed, note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("H-START", "H-END"), attempt: 2),
		];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(description: task, attempt: 2), RoundKind.Review, sequence: 7, history, Target);

		Assert.True(briefing.Length <= PyxieLimits.MaxBriefingLength, $"The briefing is {briefing.Length} characters.");
		Assert.Contains(task, BriefingSections.Get(briefing, "Task"), StringComparison.Ordinal);
		Assert.Contains("git diff release/2...HEAD", BriefingSections.Get(briefing, "Workspace"), StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.ResultFile}`", BriefingSections.Get(briefing, "When you finish"), StringComparison.Ordinal);
		int historyStart = briefing.IndexOf("## History", StringComparison.Ordinal);
		Assert.True(historyStart > 0, "The four quoted reports must leave room for the history heading.");
		string beforeHistory = briefing[..historyStart];
		foreach (string quoted in new[] { "X", "W", "H", "R" })
		{
			Assert.Contains($"{quoted}-START", beforeHistory, StringComparison.Ordinal);
			Assert.Contains($"{quoted}-END", beforeHistory, StringComparison.Ordinal);
		}

		Assert.Equal(4, CountOccurrences(beforeHistory, "[truncated: "));
		Assert.DoesNotContain("C-START", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("G-START", briefing, StringComparison.Ordinal);
	}

	private static int CountOccurrences(string text, string value)
	{
		return text.Split(value).Length - 1;
	}

	// A report as long as the storage limit that starts and ends with a distinctive marker.
	private static string Bulky(string start, string end)
	{
		return start + new string('.', PyxieLimits.MaxRoundReportLength - start.Length - end.Length) + end;
	}

	private static void AssertInOrder(string briefing, params string[] markers)
	{
		int previous = -1;
		foreach (string marker in markers)
		{
			int position = briefing.IndexOf(marker, StringComparison.Ordinal);

			Assert.True(position >= 0, $"The briefing has no '{marker}'.");
			Assert.True(position > previous, $"'{marker}' must come after the section before it.");
			previous = position;
		}
	}
}
