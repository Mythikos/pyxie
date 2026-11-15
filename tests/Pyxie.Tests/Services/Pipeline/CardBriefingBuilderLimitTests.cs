using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A briefing stays bounded after many loops and huge reports, while the latest failure still arrives in full.</summary>
public sealed class CardBriefingBuilderLimitTests
{
	private static string BuildWorkBriefing(string gateReport)
	{
		return CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 3, [TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", report: gateReport)], BriefingSections.TargetBranch);
	}

	// A report as long as the storage limit that starts and ends with a distinctive marker.
	private static string Bulky(string start, string end)
	{
		return start + new string('.', PyxieLimits.MaxRoundReportLength - start.Length - end.Length) + end;
	}

	private static int CountOccurrences(string text, string value)
	{
		return text.Split(value).Length - 1;
	}

	[Fact]
	public void Build_QuotesAReportOfOrdinaryLengthInFullWithoutATruncationMarker()
	{
		string report = new('x', 14_000);

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 3, [TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda"), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", report: report)], BriefingSections.TargetBranch);

		Assert.Contains(report, briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("[truncated:", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_KeepsTheFirstFiveThousandCharactersAndTheEndOfAHugeQuotedReportWithTheOmittedCountBetween()
	{
		// 5,000 head characters, 35,064 that go, and 9,936 tail characters: the excerpt (with its marker) must fit the 15,000 limit.
		string head = new('a', 5_000);
		string middle = new('m', 35_064);
		string tail = new('z', 9_936);

		string briefing = BuildWorkBriefing(head + middle + tail);

		Assert.Contains(head + "\n[truncated: 35064 characters omitted]\n" + tail, briefing, StringComparison.Ordinal);
		Assert.DoesNotContain(new string('m', 100), briefing, StringComparison.Ordinal);
		Assert.DoesNotContain(new string('a', 5_001), briefing, StringComparison.Ordinal);
		Assert.Equal(1, CountOccurrences(briefing, "[truncated:"));
	}

	[Fact]
	public void Build_KeepsTheQuotedExcerptOfAHugeReportWithinFifteenThousandCharacters()
	{
		string briefing = BuildWorkBriefing(new string('Q', 50_000));

		// From the first quoted character to the last: the kept head, the marker, and the kept end.
		string excerpt = briefing[briefing.IndexOf('Q', StringComparison.Ordinal)..(briefing.LastIndexOf('Q') + 1)];
		Assert.InRange(excerpt.Length, 1, 15_000);
		Assert.StartsWith(new string('Q', 5_000) + "\n[truncated: ", excerpt, StringComparison.Ordinal);
		Assert.Contains("characters omitted]", excerpt, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_KeepsTheLastLineOfAGateReportSoTheTestSummarySurvives()
	{
		string output = string.Concat(Enumerable.Repeat("PASS tests/Some.Test.cs test case ok\n", 1_200)) + "Failed!  - Failed: 2, Passed: 41, Skipped: 0, Total: 43";

		string briefing = BuildWorkBriefing(output);

		Assert.True(output.Length > 15_000, "The report must be long enough to be cut.");
		Assert.Contains("Failed!  - Failed: 2, Passed: 41, Skipped: 0, Total: 43", briefing, StringComparison.Ordinal);
		Assert.Contains(output[..5_000], briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(15_000, false)]
	[InlineData(15_001, true)]
	public void Build_CutsAQuotedReportOnlyWhenItIsLongerThanFifteenThousandCharacters(int length, bool cut)
	{
		string report = new('x', length);

		string briefing = BuildWorkBriefing(report);

		Assert.Equal(!cut, briefing.Contains(report, StringComparison.Ordinal));
		Assert.Equal(cut, briefing.Contains("[truncated:", StringComparison.Ordinal));
	}

	[Fact]
	public void Build_ForReview_CapsEachQuotedReportSeparatelyKeepingItsStartAndEnd()
	{
		string briefing = CardBriefingBuilder.Build(
			InMemoryCard.Create(),
			RoundKind.Review,
			sequence: 6,
			[
				TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: Bulky("W-START", "W-END")),
				TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed, report: Bulky("G-START", "G-END")),
				TestRound.Create(3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Not yet", report: Bulky("R-START", "R-END")),
				TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed, "Wanda", report: Bulky("V-START", "V-END")),
				TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed, report: Bulky("H-START", "H-END")),
			],
			BriefingSections.TargetBranch);

		foreach (string quoted in new[] { "V", "H", "R" })
		{
			Assert.Contains($"{quoted}-START", briefing, StringComparison.Ordinal);
			Assert.Contains($"{quoted}-END", briefing, StringComparison.Ordinal);
		}

		Assert.DoesNotContain("W-START", briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("G-START", briefing, StringComparison.Ordinal);
		Assert.Equal(3, CountOccurrences(briefing, "[truncated:"));
		Assert.True(briefing.Length <= PyxieLimits.MaxBriefingLength, $"The briefing is {briefing.Length} characters.");
	}

	[Fact]
	public void Build_AfterManyLoopsWithHugeReports_StaysWithinTheBriefingLimitAndKeepsTheLatestFailureInFull()
	{
		List<CardRound> previous = [];
		for (int loop = 0; loop < 30; loop++)
		{
			previous.Add(TestRound.Create((loop * 2) + 1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: new string('n', PyxieLimits.MaxRoundNoteLength), report: new string('w', PyxieLimits.MaxRoundReportLength)));
			string output = loop == 29 ? new string('L', 14_000) : new string('o', PyxieLimits.MaxRoundReportLength);
			previous.Add(TestRound.Create((loop * 2) + 2, RoundKind.Gate, RoundOutcome.Failed, note: new string('f', PyxieLimits.MaxRoundNoteLength), report: output));
		}

		string briefing = CardBriefingBuilder.Build(InMemoryCard.Create(), RoundKind.Work, sequence: 61, previous, BriefingSections.TargetBranch);

		Assert.True(briefing.Length <= PyxieLimits.MaxBriefingLength, $"The briefing is {briefing.Length} characters.");
		Assert.Contains(new string('L', 14_000), briefing, StringComparison.Ordinal);
		Assert.Contains("- (40 earlier rounds omitted)", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_WithAnEnormousDescription_IsCutToTheBriefingLimitAndSaysSo()
	{
		WorkCard card = InMemoryCard.Create(description: new string('d', 200_000));

		string briefing = CardBriefingBuilder.Build(card, RoundKind.Work, sequence: 1, [], BriefingSections.TargetBranch);

		Assert.InRange(briefing.Length, PyxieLimits.MaxBriefingLength - 100, PyxieLimits.MaxBriefingLength);
		Assert.EndsWith("characters omitted]", briefing, StringComparison.Ordinal);
		Assert.StartsWith("# Card 7:", briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	public void Build_NeverExceedsTheBriefingLimitEvenWhenEverythingIsHuge(RoundKind kind)
	{
		List<CardRound> previous = [];
		for (int sequence = 1; sequence <= 40; sequence++)
		{
			RoundKind roundKind = (sequence % 3) switch
			{
				1 => RoundKind.Work,
				2 => RoundKind.Gate,
				_ => RoundKind.Review,
			};
			RoundOutcome outcome = roundKind switch
			{
				RoundKind.Work => RoundOutcome.Completed,
				RoundKind.Gate => RoundOutcome.Passed,
				_ => RoundOutcome.Rejected,
			};
			previous.Add(TestRound.Create(sequence, roundKind, outcome, "Agent with a long name " + new string('a', 80), note: new string('n', PyxieLimits.MaxRoundNoteLength), report: new string('r', PyxieLimits.MaxRoundReportLength)));
		}

		WorkCard card = InMemoryCard.Create(description: new string('d', PyxieLimits.MaxDescriptionLength));

		string briefing = CardBriefingBuilder.Build(card, kind, sequence: 41, previous, BriefingSections.TargetBranch);

		Assert.True(briefing.Length <= PyxieLimits.MaxBriefingLength, $"The briefing is {briefing.Length} characters.");
	}
}
