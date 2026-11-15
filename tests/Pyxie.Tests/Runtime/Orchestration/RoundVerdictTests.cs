using System.Text.RegularExpressions;

using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;
using Pyxie.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>What a verdict records when a round ends: the note fits one line's worth, the report keeps its head and tail, and rounds that need a reason always have one.</summary>
public sealed class RoundVerdictTests
{
	private const int ReportHeadLength = 20_000;

	[Theory]
	[InlineData(nameof(RoundVerdict.Failed), RoundResult.Failed)]
	[InlineData(nameof(RoundVerdict.Outdated), RoundResult.Outdated)]
	[InlineData(nameof(RoundVerdict.Errored), RoundResult.Errored)]
	[InlineData(nameof(RoundVerdict.Blocked), RoundResult.Blocked)]
	public void Factory_KeepsTheResultNoteAndReport(string factory, RoundResult expected)
	{
		RoundVerdict verdict = factory switch
		{
			nameof(RoundVerdict.Failed) => RoundVerdict.Failed("why", "details"),
			nameof(RoundVerdict.Outdated) => RoundVerdict.Outdated("why", "details"),
			nameof(RoundVerdict.Errored) => RoundVerdict.Errored("why", "details"),
			_ => RoundVerdict.Blocked("why", "details"),
		};

		Assert.Equal(new RoundVerdict(expected, "why", "details"), verdict);
	}

	[Fact]
	public void Succeeded_HasNoReportUnlessGivenOne()
	{
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "done", null), RoundVerdict.Succeeded("done"));
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, null, "details"), RoundVerdict.Succeeded(null, "details"));
	}

	[Fact]
	public void ToRoundReport_ClipsANoteLongerThanTheLimitToExactlyTheLimitEndingInAnEllipsis()
	{
		RoundVerdict verdict = RoundVerdict.Failed(new string('n', PyxieLimits.MaxRoundNoteLength + 500));

		RoundReport report = verdict.ToRoundReport();

		Assert.NotNull(report.Note);
		Assert.Equal(PyxieLimits.MaxRoundNoteLength, report.Note.Length);
		Assert.Equal(new string('n', PyxieLimits.MaxRoundNoteLength - 1) + "…", report.Note);
	}

	[Fact]
	public void ToRoundReport_KeepsANoteThatIsExactlyTheLimit()
	{
		string note = new('n', PyxieLimits.MaxRoundNoteLength);

		RoundReport report = RoundVerdict.Failed(note).ToRoundReport();

		Assert.Equal(note, report.Note);
	}

	[Fact]
	public void ToRoundReport_TrimsTheNote()
	{
		RoundReport report = RoundVerdict.Failed("  needs a test \r\n").ToRoundReport();

		Assert.Equal("needs a test", report.Note);
	}

	[Fact]
	public void ToRoundReport_KeepsAReportThatFitsTheLimitExactlyAsItIs()
	{
		string report = "  details\n" + new string('r', PyxieLimits.MaxRoundReportLength - "  details\n".Length);

		RoundReport recorded = RoundVerdict.Succeeded("done", report).ToRoundReport();

		Assert.Equal(PyxieLimits.MaxRoundReportLength, report.Length);
		Assert.Equal(report, recorded.Report);
	}

	[Fact]
	public void ToRoundReport_ShortensALongReportToItsHeadAndItsTailWithAMarkerForTheMiddle()
	{
		// The middle holds the only 'M' characters, so none of them may survive while the head and the tail do.
		string head = new('A', ReportHeadLength);
		string middle = new('M', 100_000);
		string tail = new('Z', 100_000);
		string original = head + middle + tail;

		RoundReport recorded = RoundVerdict.Succeeded("done", original).ToRoundReport();

		string text = recorded.Report ?? string.Empty;
		Assert.True(text.Length <= PyxieLimits.MaxRoundReportLength, $"The report is {text.Length} characters; the limit is {PyxieLimits.MaxRoundReportLength}.");
		Assert.StartsWith(head, text, StringComparison.Ordinal);
		Assert.EndsWith(new string('Z', 1000), text, StringComparison.Ordinal);
		Assert.DoesNotContain('M', text);
		Match marker = Regex.Match(text, @"\[truncated: (\d+) characters omitted\]");
		Assert.True(marker.Success, "A shortened report says that the middle was cut.");
		int keptTail = text.Length - text.LastIndexOf('\n') - 1;
		Assert.Equal(original.Length, ReportHeadLength + int.Parse(marker.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + keptTail);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  \r\n\t ")]
	public void ToRoundReport_RecordsNoReportForABlankOne(string? report)
	{
		RoundReport recorded = RoundVerdict.Succeeded("done", report).ToRoundReport();

		Assert.Null(recorded.Report);
	}

	[Theory]
	[InlineData(RoundResult.Failed, "The round failed without saying why.")]
	[InlineData(RoundResult.Errored, "The round broke without saying why.")]
	[InlineData(RoundResult.Blocked, "The round needs a person without saying why.")]
	public void ToRoundReport_GivesRoundsThatNeedAReasonADefaultNoteWhenTheyHaveNone(RoundResult result, string expected)
	{
		Assert.Equal(expected, new RoundVerdict(result, null, null).ToRoundReport().Note);
		Assert.Equal(expected, new RoundVerdict(result, string.Empty, null).ToRoundReport().Note);
		Assert.Equal(expected, new RoundVerdict(result, "  \t\r\n", null).ToRoundReport().Note);
	}

	[Theory]
	[InlineData(RoundResult.Failed)]
	[InlineData(RoundResult.Errored)]
	[InlineData(RoundResult.Blocked)]
	public void ToRoundReport_KeepsTheNoteOfARoundThatNeedsOne(RoundResult result)
	{
		RoundReport recorded = new RoundVerdict(result, "the real reason", "details").ToRoundReport();

		Assert.Equal(new RoundReport("the real reason", "details"), recorded);
	}

	[Theory]
	[InlineData(RoundResult.Succeeded)]
	[InlineData(RoundResult.Outdated)]
	public void ToRoundReport_RecordsNoNoteForRoundsThatNeedNoneWhenTheyHaveNone(RoundResult result)
	{
		Assert.Null(new RoundVerdict(result, null, null).ToRoundReport().Note);
		Assert.Null(new RoundVerdict(result, "   ", null).ToRoundReport().Note);
	}

	[Theory]
	[InlineData(RoundResult.Succeeded)]
	[InlineData(RoundResult.Outdated)]
	public void ToRoundReport_KeepsTheNoteOfARoundThatNeedsNone(RoundResult result)
	{
		Assert.Equal("a note", new RoundVerdict(result, "a note", null).ToRoundReport().Note);
	}
}
