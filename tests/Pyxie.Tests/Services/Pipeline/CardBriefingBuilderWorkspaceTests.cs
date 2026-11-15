using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>Every briefing says where the agent works and how to report, right after the task and before any context, using the words of the pipeline contract.</summary>
public sealed class CardBriefingBuilderWorkspaceTests
{
	private static string Build(RoundKind kind, string targetBranch = BriefingSections.TargetBranch, int cardId = 7, params CardRound[] previous)
	{
		return CardBriefingBuilder.Build(InMemoryCard.Create(id: cardId), kind, sequence: previous.Length + 1, previous, targetBranch);
	}

	[Fact]
	public void Build_ForWork_NamesTheCardBranchAndTheTargetBranchItStartedFromAndIsMergedInto()
	{
		string workspace = BriefingSections.Get(Build(RoundKind.Work, targetBranch: "release/1.0", cardId: 12), "Workspace");

		Assert.Contains("branch `pyxie/card-12`", workspace, StringComparison.Ordinal);
		Assert.Contains("started from `release/1.0`", workspace, StringComparison.Ordinal);
		Assert.Contains("merges `pyxie/card-12` into `release/1.0`", workspace, StringComparison.Ordinal);
		Assert.Contains("commits whatever you leave", workspace, StringComparison.Ordinal);
		Assert.Contains("do not push", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForReview_NamesTheBranchUnderReviewAndHowToCompareItWithTheTargetBranch()
	{
		string workspace = BriefingSections.Get(Build(RoundKind.Review, targetBranch: "develop", cardId: 12), "Workspace");

		Assert.Contains("branch `pyxie/card-12`", workspace, StringComparison.Ordinal);
		Assert.Contains("compared with `develop`", workspace, StringComparison.Ordinal);
		Assert.Contains("git diff develop...HEAD", workspace, StringComparison.Ordinal);
		Assert.Contains("Do not change files or push", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForReview_SaysTheFilesThatSteerAnAgentAreAsOnTheTargetBranchAndToReviewTheCardsChangesToThemWithGitDiff()
	{
		string workspace = BriefingSections.Get(Build(RoundKind.Review, targetBranch: "develop"), "Workspace");

		Assert.Contains("Files that steer an agent (", workspace, StringComparison.Ordinal);
		Assert.Contains(") are as on `develop` here", workspace, StringComparison.Ordinal);
		Assert.Contains("review the card's changes to them with git diff", workspace, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForReview_NamesOnlyFilesTheReviewSandboxTakesFromTheTargetBranch()
	{
		string workspace = BriefingSections.Get(Build(RoundKind.Review), "Workspace");
		const string Opening = "Files that steer an agent (";
		int start = workspace.IndexOf(Opening, StringComparison.Ordinal) + Opening.Length;
		int end = workspace.IndexOf(')', start);
		string[] named = workspace[start..end].Split(", ");

		Assert.True(named.Length >= 5, $"The briefing should name the usual steering files, but named: {string.Join(" | ", named)}");
		foreach (string name in named)
		{
			string path = name.EndsWith('/') ? name + "settings.json" : name;

			Assert.True(SteeringFiles.Contains(path), $"The briefing tells the reviewer that {name} is as on the target branch, but the review sandbox does not take it from there.");
		}
	}

	[Fact]
	public void Build_ForWork_SaysNothingAboutSteeringFilesBecauseTheWorkerKeepsTheCardsOwn()
	{
		string workspace = BriefingSections.Get(Build(RoundKind.Work, targetBranch: "develop"), "Workspace");

		Assert.DoesNotContain("steer", workspace, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("are as on `develop`", workspace, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public void Build_NamesTheBranchOfTheCardInTheBriefing_NotOfSomeOtherCard(RoundKind kind)
	{
		string workspace = BriefingSections.Get(Build(kind, cardId: 3), "Workspace");

		Assert.Contains(PipelineContract.BranchName(3), workspace, StringComparison.Ordinal);
		Assert.DoesNotContain(PipelineContract.BranchName(7), workspace, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public void Build_TellsTheAgentWhereToWriteTheResultAndShowsItsShapeWithTheFirstWordOfTheKind(RoundKind kind)
	{
		string finish = BriefingSections.Get(Build(kind), "When you finish");

		Assert.Contains($"Write `{PipelineContract.ResultFile}`", finish, StringComparison.Ordinal);
		Assert.Contains("```json", finish, StringComparison.Ordinal);
		Assert.Contains($"\"outcome\": \"{PipelineContract.OutcomeWords(kind)[0]}\"", finish, StringComparison.Ordinal);
		Assert.Contains("\"note\"", finish, StringComparison.Ordinal);
		Assert.Contains("\"report\"", finish, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Work, RoundKind.Review)]
	[InlineData(RoundKind.Work, RoundKind.Resolve)]
	[InlineData(RoundKind.Review, RoundKind.Work)]
	[InlineData(RoundKind.Review, RoundKind.Resolve)]
	[InlineData(RoundKind.Resolve, RoundKind.Work)]
	[InlineData(RoundKind.Resolve, RoundKind.Review)]
	public void Build_ListsEveryOutcomeWordOfTheKindAndNoneOfTheOtherKindsOwn(RoundKind kind, RoundKind other)
	{
		string finish = BriefingSections.Get(Build(kind), "When you finish");

		foreach (string word in PipelineContract.OutcomeWords(kind))
		{
			Assert.Contains($"- `{word}`:", finish, StringComparison.Ordinal);
		}

		foreach (string word in PipelineContract.OutcomeWords(other).Except(PipelineContract.OutcomeWords(kind)))
		{
			Assert.DoesNotContain($"`{word}`", finish, StringComparison.Ordinal);
		}
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public void Build_SaysABlockedCardWaitsForAPerson(RoundKind kind)
	{
		string finish = BriefingSections.Get(Build(kind), "When you finish");

		Assert.Contains($"`{PipelineContract.Blocked}`", finish, StringComparison.Ordinal);
		Assert.Contains("the card stops until someone retries it", finish, StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.Errored}`", finish, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ForWork_PutsTheTaskThenTheWorkspaceThenTheResultFormatThenTheContextThenTheHistory()
	{
		string briefing = CardBriefingBuilder.Build(
			InMemoryCard.Create(attempt: 3),
			RoundKind.Work,
			sequence: 5,
			[
				TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
				TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", report: "CS1002"),
				TestRound.Create(3, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "Timed out", attempt: 2),
				TestRound.Create(4, RoundKind.Work, RoundOutcome.Interrupted, "Wanda", note: "Pyxie stopped", attempt: 2),
			],
			BriefingSections.TargetBranch);

		AssertInOrder(briefing, "# Card 7", "## Task", "## Workspace", "## When you finish", "## Why this card came back", "## How the previous attempt ended", "## History");
	}

	[Fact]
	public void Build_ForReview_PutsTheTaskThenTheWorkspaceThenTheResultFormatThenTheContextThenTheHistory()
	{
		string briefing = CardBriefingBuilder.Build(
			InMemoryCard.Create(attempt: 2),
			RoundKind.Review,
			sequence: 6,
			[
				TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "First handoff"),
				TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: "First gates"),
				TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Missing a test", report: "Findings"),
				TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: "Second handoff", attempt: 2),
				TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed, report: "Second gates", attempt: 2),
			],
			BriefingSections.TargetBranch);

		AssertInOrder(briefing, "# Card 7", "## Task", "## Workspace", "## When you finish", "## Work to review", "## Gate results", "## Previous review findings", "## History");
	}

	[Fact]
	public void Build_WithTheLargestContextTheLimitsAllow_KeepsTheTaskTheWorkspaceAndTheResultFormatIntact()
	{
		List<CardRound> previous = [];
		for (int sequence = 1; sequence <= 40; sequence++)
		{
			RoundKind kind = (sequence % 3) switch
			{
				1 => RoundKind.Work,
				2 => RoundKind.Gate,
				_ => RoundKind.Review,
			};
			RoundOutcome outcome = kind switch
			{
				RoundKind.Work => RoundOutcome.Completed,
				RoundKind.Gate => RoundOutcome.Passed,
				_ => RoundOutcome.Rejected,
			};
			previous.Add(TestRound.Create(sequence, kind, outcome, "Wanda", note: new string('n', PyxieLimits.MaxRoundNoteLength), report: new string('r', PyxieLimits.MaxRoundReportLength)));
		}

		string task = new('d', PyxieLimits.MaxDescriptionLength);

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(description: task), RoundKind.Review, sequence: 41, previous, BriefingSections.TargetBranch);

		Assert.True(briefing.Length <= PyxieLimits.MaxBriefingLength, $"The briefing is {briefing.Length} characters.");
		Assert.Contains(task, BriefingSections.Get(briefing, "Task"), StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.ResultFile}`", BriefingSections.Get(briefing, "When you finish"), StringComparison.Ordinal);
		Assert.Contains("git diff main...HEAD", BriefingSections.Get(briefing, "Workspace"), StringComparison.Ordinal);
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
