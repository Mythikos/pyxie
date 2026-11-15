using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>What every briefing carries, whatever the round: the card, the stage and attempt, the task, and one line per earlier round.</summary>
public sealed class CardBriefingBuilderHeaderTests
{
	private static List<string> HistoryLines(string briefing)
	{
		return [.. briefing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => line.StartsWith("- Round ", StringComparison.Ordinal))];
	}

	[Fact]
	public void Build_StatesTheCardItsCategoryTheStageTheAttemptAndTheTask()
	{
		WorkCard card = InMemoryCard.Create(id: 7, name: "Fix the login", description: "Users cannot sign in with a plus sign in their address.", categoryName: "Bug", attempt: 1, attemptLimit: 3);

		string briefing = CardBriefingBuilder.Build(card, RoundKind.Work, sequence: 1, [], BriefingSections.TargetBranch);

		Assert.Contains("# Card 7: Fix the login", briefing, StringComparison.Ordinal);
		Assert.Contains("- Category: Bug", briefing, StringComparison.Ordinal);
		Assert.Contains("- Stage: Work (attempt 1 of 3; round 1 of this card)", briefing, StringComparison.Ordinal);
		Assert.Contains("## Task", briefing, StringComparison.Ordinal);
		Assert.Contains("Users cannot sign in with a plus sign in their address.", briefing, StringComparison.Ordinal);
		Assert.True(briefing.IndexOf("## Task", StringComparison.Ordinal) < briefing.IndexOf("Users cannot sign in", StringComparison.Ordinal), "The description belongs under the Task heading.");
	}

	[Fact]
	public void Build_OfAFirstRound_HasNoHistory()
	{
		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 1, [], BriefingSections.TargetBranch);

		Assert.DoesNotContain("## History", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Why this card came back", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Work, 1, 3, "- Stage: Work (attempt 1 of 3; round 4 of this card)")]
	[InlineData(RoundKind.Work, 2, 3, "- Stage: Work (attempt 2 of 3; round 4 of this card)")]
	[InlineData(RoundKind.Work, 3, 3, "- Stage: Work (attempt 3 of 3; round 4 of this card)")]
	[InlineData(RoundKind.Work, 1, 1, "- Stage: Work (attempt 1 of 1; round 4 of this card)")]
	[InlineData(RoundKind.Review, 2, 7, "- Stage: Review (attempt 2 of 7; round 4 of this card)")]
	[InlineData(RoundKind.Review, 10, 10, "- Stage: Review (attempt 10 of 10; round 4 of this card)")]
	[InlineData(RoundKind.Resolve, 2, 7, "- Stage: Resolve (attempt 2 of 7; round 4 of this card)")]
	public void Build_StatesTheCardsAttemptOfItsLimitAndTheRoundOfTheWholeHistory(RoundKind kind, int attempt, int limit, string expected)
	{
		WorkCard card = InMemoryCard.Create(attempt: attempt, attemptLimit: limit);

		string briefing = CardBriefingBuilder.Build(card, kind, sequence: 4, [], BriefingSections.TargetBranch);

		Assert.Contains(expected, briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_TakesTheAttemptFromTheCardNotFromCountingEarlierRounds()
	{
		List<CardRound> previous =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed"),
			TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Wanda", attempt: 2),
			TestRound.Create(4, RoundKind.Gate, RoundOutcome.Failed, note: "Tests failed", attempt: 2),
		];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(attempt: 1, attemptLimit: 3), RoundKind.Work, sequence: 5, previous, BriefingSections.TargetBranch);

		Assert.Contains("- Stage: Work (attempt 1 of 3; round 5 of this card)", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Merge)]
	[InlineData((RoundKind)99)]
	public void Build_OfARoundThatRunsNoAgent_Throws(RoundKind kind)
	{
		ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => CardBriefingBuilder.Build(InMemoryCard.Create(), kind, sequence: 1, [], BriefingSections.TargetBranch));

		Assert.Equal("kind", error.ParamName);
	}

	[Fact]
	public void Build_ListsEveryEarlierRoundInSequenceOrderWithItsAttemptAgentAndNote()
	{
		List<CardRound> previous =
		[
			TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Walt", attempt: 2),
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "2 tests failed"),
			TestRound.Create(4, RoundKind.Gate, RoundOutcome.Passed, attempt: 2),
			TestRound.Create(5, RoundKind.Review, RoundOutcome.Errored, "Rita", note: "Harness crashed", attempt: 2),
			TestRound.Create(6, RoundKind.Review, RoundOutcome.Skipped, note: "No reviewer is assigned.", attempt: 3),
			TestRound.Create(7, RoundKind.Merge, RoundOutcome.Conflicted, note: "Conflict in Login.cs", attempt: 3),
		];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(attempt: 3), RoundKind.Work, sequence: 8, previous, BriefingSections.TargetBranch);

		Assert.Equal(
			[
				"- Round 1, attempt 1, Work, Completed by Wanda",
				"- Round 2, attempt 1, Gates, Failed: 2 tests failed",
				"- Round 3, attempt 2, Work, Completed by Walt",
				"- Round 4, attempt 2, Gates, Passed",
				"- Round 5, attempt 2, Review, Errored by Rita: Harness crashed",
				"- Round 6, attempt 3, Review, Skipped: No reviewer is assigned.",
				"- Round 7, attempt 3, Merge, Conflicted: Conflict in Login.cs",
			],
			HistoryLines(briefing));
	}

	[Fact]
	public void Build_ShowsEachNoteOnOneLineInTheHistory()
	{
		List<CardRound> previous = [TestRound.Create(1, RoundKind.Work, RoundOutcome.Errored, "Wanda", note: "first line\r\nsecond line\n\n   third line  ")];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(attempt: 2), RoundKind.Work, sequence: 2, previous, BriefingSections.TargetBranch);

		Assert.Contains("- Round 1, attempt 1, Work, Errored by Wanda: first line second line third line", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_CutsALongNoteShortInTheHistoryAndSaysSo()
	{
		string longNote = new('n', 500);
		List<CardRound> previous =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Errored, note: longNote),
		];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(attempt: 2), RoundKind.Work, sequence: 3, previous, BriefingSections.TargetBranch);

		Assert.DoesNotContain(new string('n', 300), briefing, StringComparison.Ordinal);
		string line = Assert.Single(HistoryLines(briefing), historyLine => historyLine.StartsWith("- Round 2, ", StringComparison.Ordinal));
		Assert.StartsWith("- Round 2, attempt 1, Gates, Errored: nnnn", line, StringComparison.Ordinal);
		Assert.EndsWith("n…", line, StringComparison.Ordinal);
		Assert.True(line.Length <= "- Round 2, attempt 1, Gates, Errored: ".Length + 200, $"A clipped note must stay within 200 characters on its one history line, but the line was {line.Length} characters.");
	}

	[Fact]
	public void Build_SaysOneEarlierRoundWasLeftOutInTheSingular()
	{
		List<CardRound> previous = [.. Enumerable.Range(1, 21).Select(sequence => TestRound.Create(sequence, RoundKind.Work, RoundOutcome.Completed, "Wanda"))];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 22, previous, BriefingSections.TargetBranch);

		Assert.Contains("- (1 earlier round omitted)", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ListsOnlyTheLatestTwentyRoundsAndSaysHowManyWereLeftOut()
	{
		List<CardRound> previous = [.. Enumerable.Range(1, 25).Select(sequence => TestRound.Create(sequence, RoundKind.Work, RoundOutcome.Completed, "Wanda"))];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 26, previous, BriefingSections.TargetBranch);

		List<string> lines = HistoryLines(briefing);
		Assert.Equal(20, lines.Count);
		Assert.StartsWith("- Round 6, ", lines[0], StringComparison.Ordinal);
		Assert.StartsWith("- Round 25, ", lines[^1], StringComparison.Ordinal);
		Assert.Contains("- (5 earlier rounds omitted)", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ListsTwentyRoundsWithoutAnOmittedMarker()
	{
		List<CardRound> previous = [.. Enumerable.Range(1, 20).Select(sequence => TestRound.Create(sequence, RoundKind.Work, RoundOutcome.Completed, "Wanda"))];

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 21, previous, BriefingSections.TargetBranch);

		Assert.Equal(20, HistoryLines(briefing).Count);
		Assert.DoesNotContain("omitted", briefing, StringComparison.Ordinal);
	}
}
