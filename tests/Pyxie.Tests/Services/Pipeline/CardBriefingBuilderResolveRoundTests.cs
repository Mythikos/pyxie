using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A merge agent is told what to do and what not to do in its merge, why the merge stopped and what landed on the target branch, how the card's own work was handed off, and how its last attempt ended when that attempt did not finish. It is given no review context.</summary>
public sealed class CardBriefingBuilderResolveRoundTests
{
	private const string Target = "release/2";

	private static CardRound[] ConflictedHistory(string note = "Merging release/2 conflicted in src/Login.cs", string? report = "CONFLICT (content): Merge conflict in src/Login.cs", string workReport = "WORKHANDOFF")
	{
		return
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: workReport),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "GATEOUTPUT"),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Rita", note: "Looks right", report: "REVIEWOUTPUT"),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: note, report: report),
		];
	}

	private static string BuildResolve(int cardId = 12, int attempt = 2, params CardRound[] previous)
	{
		return CardBriefingBuilder.Build(InMemoryCard.Create(id: cardId, attempt: attempt), RoundKind.Resolve, sequence: previous.Length + 1, previous, Target);
	}

	[Fact]
	public void Build_StatesTheResolveStageWithItsAttemptAndRound()
	{
		string briefing = BuildResolve(12, 2, ConflictedHistory());

		Assert.Contains("# Card 12: Fix the login", briefing, StringComparison.Ordinal);
		Assert.Contains("- Stage: Resolve (attempt 2 of 5; round 5 of this card)", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_TellsTheMergeAgentItIsInTheCardsBranchWithTheMergeOfTheTargetStartedAndStoppedOnConflicts()
	{
		string workspace = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "Workspace");

		Assert.Contains("on branch `pyxie/card-12`, which holds the card's work", workspace, StringComparison.Ordinal);
		Assert.Contains("Pyxie has started merging `release/2` into it", workspace, StringComparison.Ordinal);
		Assert.Contains("the merge stopped on conflicts", workspace, StringComparison.Ordinal);
		Assert.Contains("`git status` lists them", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_GivesTheMergeAgentANarrowBriefToReconcileBothSidesAndAddNothing()
	{
		string workspace = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "Workspace");

		Assert.Contains("the card's change and the changes on `release/2` both still hold", workspace, StringComparison.Ordinal);
		Assert.Contains("Keep the intent of both sides and add nothing else", workspace, StringComparison.Ordinal);
		Assert.Contains("fix it as part of the merge", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_SaysPyxieCommitsTheMergeOrTheAgentCanCommitItWithoutAnEditor()
	{
		string workspace = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "Workspace");

		Assert.Contains("Pyxie commits the merge when you finish", workspace, StringComparison.Ordinal);
		Assert.Contains("`git commit --no-edit`", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_SaysTheMergeMustComeBackAsOneMergeCommitOfTheTwoBranchesAndWhatNotToDo()
	{
		string workspace = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "Workspace");

		Assert.Contains("one merge commit of `pyxie/card-12` and `release/2`", workspace, StringComparison.Ordinal);
		Assert.Contains("do not abort it, start over, rebase, make other commits, or push", workspace, StringComparison.Ordinal);
		Assert.Contains("The gates and the review check the resolution next.", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_NamesTheBranchOfTheCardInTheBriefingNotOfSomeOtherCard()
	{
		string workspace = BriefingSections.Get(BuildResolve(3, 2, ConflictedHistory()), "Workspace");

		Assert.Contains(PipelineContract.BranchName(3), workspace, StringComparison.Ordinal);
		Assert.DoesNotContain(PipelineContract.BranchName(12), workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_DoesNotUseTheWorkOrReviewWorkspaceText()
	{
		string workspace = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "Workspace");

		Assert.DoesNotContain("started from", workspace, StringComparison.Ordinal);
		Assert.DoesNotContain("Make the change here", workspace, StringComparison.Ordinal);
		Assert.DoesNotContain("git diff", workspace, StringComparison.Ordinal);
		Assert.DoesNotContain("steer", workspace, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Build_TellsTheAgentWhereToWriteTheResultAndShowsItsShapeWithResolved()
	{
		string finish = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "When you finish");

		Assert.Contains($"Write `{PipelineContract.ResultFile}`", finish, StringComparison.Ordinal);
		Assert.Contains("```json", finish, StringComparison.Ordinal);
		Assert.Contains("\"outcome\": \"resolved\"", finish, StringComparison.Ordinal);
		Assert.Contains("\"note\"", finish, StringComparison.Ordinal);
		Assert.Contains("\"report\"", finish, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ListsExactlyTheWordsAResolveRoundMayReportAndNoneOfTheOtherKindsOwn()
	{
		string finish = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "When you finish");

		foreach (string word in PipelineContract.OutcomeWords(RoundKind.Resolve))
		{
			Assert.Contains($"- `{word}`:", finish, StringComparison.Ordinal);
		}

		Assert.Equal(PipelineContract.OutcomeWords(RoundKind.Resolve).Count, finish.Split('\n').Count(line => line.StartsWith("- `", StringComparison.Ordinal)));
		foreach (string word in new[] { PipelineContract.Completed, PipelineContract.Approved, PipelineContract.Rejected })
		{
			Assert.DoesNotContain($"`{word}`", finish, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Build_AsksTheMergeAgentToHandOffToTheReviewerWhoWillReadTheResolution()
	{
		string finish = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "When you finish");

		Assert.Contains("every conflict is resolved", finish, StringComparison.Ordinal);
		Assert.Contains("hand off to the reviewer: how you resolved each conflict and why", finish, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_SaysABlockedMergeWaitsForAPersonAndGivesAResolveSpecificExample()
	{
		string finish = BriefingSections.Get(BuildResolve(12, 2, ConflictedHistory()), "When you finish");

		Assert.Contains($"`{PipelineContract.Blocked}`", finish, StringComparison.Ordinal);
		Assert.Contains("the card stops until someone retries it", finish, StringComparison.Ordinal);
		Assert.Contains("the card's change and the changes on the target branch cannot both hold", finish, StringComparison.Ordinal);
		Assert.DoesNotContain("the task is unclear", finish, StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.Errored}`", finish, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesWhyTheMergeStoppedWithItsNoteAndReportInFull()
	{
		string files = string.Concat(Enumerable.Range(1, 30).Select(file => $"CONFLICT (content): Merge conflict in src/File{file}.cs\n"));

		string briefing = BuildResolve(12, 2, ConflictedHistory(note: "Merging release/2 conflicted in 30 files", report: files));

		string section = BriefingSections.Get(briefing, "Why the merge stopped");
		Assert.Contains("Round 4, attempt 1, Merge, Conflicted.", section, StringComparison.Ordinal);
		Assert.Contains("Merging release/2 conflicted in 30 files", section, StringComparison.Ordinal);
		Assert.Contains(files, section, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesTheMergeReportWithWhatLandedOnTheTargetBranchAsTheMergeRoundRecordedIt()
	{
		string report = "Conflicting files:\n- src/Login.cs\n\nLanded on release/2 since this branch last took it in:\n- 1a2b3c4 Merge pyxie/card-9: Rework the form\n  Make the form accept plus signs.";

		string briefing = BuildResolve(12, 2, ConflictedHistory(report: report));

		Assert.Contains(report, BriefingSections.Get(briefing, "Why the merge stopped"), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesOnlyTheLatestConflict()
	{
		CardRound[] history =
		[
			.. ConflictedHistory(note: "FIRSTCONFLICTNOTE", report: "FIRSTCONFLICTREPORT"),
			TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", report: "FIRSTRESOLUTION", attempt: 2),
			TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed, attempt: 2),
			TestRound.Create(7, RoundKind.Review, RoundOutcome.Approved, "Rita", attempt: 2),
			TestRound.Create(8, RoundKind.Merge, RoundOutcome.Conflicted, note: "SECONDCONFLICTNOTE", report: "SECONDCONFLICTREPORT", attempt: 2),
		];

		string section = BriefingSections.Get(BuildResolve(12, 3, history), "Why the merge stopped");

		Assert.Contains("SECONDCONFLICTNOTE", section, StringComparison.Ordinal);
		Assert.Contains("SECONDCONFLICTREPORT", section, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTCONFLICTREPORT", section, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTCONFLICTNOTE", section, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_QuotesTheLatestCompletedWorkHandoffAsTheCardsWork()
	{
		CardRound[] history =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "OLDHANDOFF"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Lint errors"),
			TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Walt", note: "Fixed the lint errors", report: "NEWHANDOFF", attempt: 2),
			TestRound.Create(4, RoundKind.Work, RoundOutcome.Errored, "Walt", note: "Crashed", report: "ERROREDWORK", attempt: 2),
			TestRound.Create(5, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict", attempt: 2),
		];

		string section = BriefingSections.Get(BuildResolve(12, 3, history), "The card's work");

		Assert.Contains("Round 3, attempt 2, Work, Completed by Walt.", section, StringComparison.Ordinal);
		Assert.Contains("Fixed the lint errors", section, StringComparison.Ordinal);
		Assert.Contains("NEWHANDOFF", section, StringComparison.Ordinal);
		Assert.DoesNotContain("OLDHANDOFF", section, StringComparison.Ordinal);
		Assert.DoesNotContain("ERROREDWORK", section, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WithoutACompletedWorkRound_HasNoSectionForTheCardsWork()
	{
		string briefing = BuildResolve(12, 2, TestRound.Create(1, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict"));

		Assert.DoesNotContain("## The card's work", briefing, StringComparison.Ordinal);
		Assert.Contains("## Why the merge stopped", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundOutcome.Errored, "the merge agent crashed")]
	[InlineData(RoundOutcome.Blocked, "Needs a person to say which side wins.")]
	[InlineData(RoundOutcome.Interrupted, "Pyxie stopped while this round was running.")]
	public void Build_AfterAResolveAttemptThatDidNotFinish_ShowsHowItEnded(RoundOutcome outcome, string note)
	{
		CardRound[] history = [.. ConflictedHistory(), TestRound.Create(5, RoundKind.Resolve, outcome, "Merger", note: note, report: "partial resolution so far", attempt: 2)];

		string section = BriefingSections.Get(BuildResolve(12, 3, history), "How the previous attempt ended");

		Assert.Contains($"Round 5, attempt 2, Resolve, {outcome} by Merger.", section, StringComparison.Ordinal);
		Assert.Contains(note, section, StringComparison.Ordinal);
		Assert.Contains("partial resolution so far", section, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AfterAResolveAttemptThatResolved_HasNoEndingSection()
	{
		CardRound[] history = [.. ConflictedHistory(), TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", note: "Combined both", attempt: 2)];

		Assert.DoesNotContain("## How the previous attempt ended", BuildResolve(12, 3, history), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WhenTheLastRoundIsAnotherKindsUnfinishedRound_HasNoEndingSection()
	{
		CardRound[] history = [.. ConflictedHistory(), TestRound.Create(5, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Crashed", attempt: 2)];

		Assert.DoesNotContain("## How the previous attempt ended", BuildResolve(12, 3, history), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WhenAnEarlierResolveAttemptErroredButALaterRoundRan_HasNoEndingSection()
	{
		CardRound[] history =
		[
			.. ConflictedHistory(),
			TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Errored, "Merger", note: "Crashed", attempt: 2),
			TestRound.Create(6, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", attempt: 3),
			TestRound.Create(7, RoundKind.Gate, RoundOutcome.Passed, attempt: 3),
		];

		Assert.DoesNotContain("## How the previous attempt ended", BuildResolve(12, 3, history), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_PutsTheTaskThenTheWorkspaceThenTheResultFormatThenTheConflictThenTheWorkThenHowTheLastAttemptEndedThenTheHistory()
	{
		CardRound[] history = [.. ConflictedHistory(), TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Errored, "Merger", note: "Crashed", attempt: 2)];

		string briefing = BuildResolve(12, 3, history);

		AssertInOrder(briefing, "# Card 12", "## Task", "## Workspace", "## When you finish", "## Why the merge stopped", "## The card's work", "## How the previous attempt ended", "## History");
	}

	[Fact]
	public void Build_NeverIncludesTheReviewersOrTheWorkersSections()
	{
		CardRound[] history =
		[
			.. ConflictedHistory(),
			TestRound.Create(5, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Old finding", report: "OLDFINDINGS"),
		];

		string briefing = BuildResolve(12, 2, history);

		Assert.DoesNotContain("## Work to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Merge to review", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## The resolution", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Gate results", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Previous review findings", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Why this card came back", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("GATEOUTPUT", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("OLDFINDINGS", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ListsAResolveRoundInTheHistoryWithTheMergeAgentThatRanIt()
	{
		CardRound[] history = [.. ConflictedHistory(), TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Errored, "Merger", note: "Crashed", attempt: 2)];

		string section = BriefingSections.Get(BuildResolve(12, 3, history), "History");

		Assert.Contains("- Round 4, attempt 1, Merge, Conflicted: Merging release/2 conflicted in src/Login.cs", section, StringComparison.Ordinal);
		Assert.Contains("- Round 5, attempt 2, Resolve, Errored by Merger: Crashed", section, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForAFirstResolveRoundWithoutAnyHistory_StillStatesTheTaskAndTheWorkspace()
	{
		string briefing = BuildResolve(12, 2);

		Assert.Contains("## Task", briefing, StringComparison.Ordinal);
		Assert.Contains("## Workspace", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## History", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WithTheLargestContextTheLimitsAllow_KeepsTheTaskTheWorkspaceAndTheResultFormatIntactAndTheConflictInFull()
	{
		string task = new('d', PyxieLimits.MaxDescriptionLength);
		CardRound[] history =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: new string('n', PyxieLimits.MaxRoundNoteLength), report: Bulky("W-START", "W-END")),
			TestRound.Create(2, RoundKind.Merge, RoundOutcome.Conflicted, note: new string('c', PyxieLimits.MaxRoundNoteLength), report: Bulky("C-START", "C-END")),
			TestRound.Create(3, RoundKind.Resolve, RoundOutcome.Errored, "Merger", note: new string('e', PyxieLimits.MaxRoundNoteLength), report: Bulky("E-START", "E-END"), attempt: 2),
		];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(description: task, attempt: 3), RoundKind.Resolve, sequence: 4, history, Target);

		Assert.True(briefing.Length <= PyxieLimits.MaxBriefingLength, $"The briefing is {briefing.Length} characters.");
		Assert.Contains(task, BriefingSections.Get(briefing, "Task"), StringComparison.Ordinal);
		Assert.Contains("git commit --no-edit", BriefingSections.Get(briefing, "Workspace"), StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.ResultFile}`", BriefingSections.Get(briefing, "When you finish"), StringComparison.Ordinal);
		foreach (string quoted in new[] { "C", "W", "E" })
		{
			Assert.Contains($"{quoted}-START", briefing, StringComparison.Ordinal);
			Assert.Contains($"{quoted}-END", briefing, StringComparison.Ordinal);
		}
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
