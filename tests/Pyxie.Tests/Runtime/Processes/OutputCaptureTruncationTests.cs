using System.Globalization;
using System.Text.RegularExpressions;

using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>What <see cref="OutputCapture"/> keeps when the output is larger than its budget: the beginning, the end, and an exact count of what was cut between them.</summary>
public sealed partial class OutputCaptureTruncationTests
{
	[GeneratedRegex(@"\[truncated: (\d+) characters omitted\](\n|\z)")]
	private static partial Regex MarkerPattern();

	[Theory]
	[InlineData(9, 100, 90)]
	[InlineData(9, 100, 999)]
	[InlineData(9, 100, 500)]
	[InlineData(200, 1, 50)]
	[InlineData(200, 5, 7)]
	[InlineData(3, 1000, 10)]
	[InlineData(1, 1000, 2)]
	[InlineData(1, 1000, 1)]
	public void ToString_WhenTheOutputIsLargerThanTheBudget_KeepsTheStartAndTheEndAndCountsWhatWasCut(int lineLength, int lineCount, int maxLength)
	{
		string[] lines = NumberedLines(lineLength, lineCount);
		OutputCapture capture = Capture(maxLength, lines);
		string everything = string.Concat(lines.Select(line => line + "\n"));

		string excerpt = capture.ToString();

		Match marker = MarkerPattern().Match(excerpt);
		Assert.True(marker.Success, $"The excerpt of {everything.Length} characters in a budget of {maxLength} has no truncation marker: {excerpt}");
		string head = excerpt[..marker.Index];
		string tail = excerpt[(marker.Index + marker.Length)..];
		long omitted = long.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture);

		// The excerpt ends where the last line did, so its final line break was trimmed.
		Assert.StartsWith(head, everything, StringComparison.Ordinal);
		Assert.EndsWith(tail + "\n", everything, StringComparison.Ordinal);
		long kept = head.Length + tail.Length + 1;
		Assert.True(kept == maxLength, $"The excerpt keeps {kept} characters; the budget is {maxLength}.");
		Assert.True(omitted == everything.Length - maxLength, $"The marker says {omitted} characters were omitted; {everything.Length - maxLength} were.");
		Assert.True(excerpt.Length <= maxLength + marker.Length, $"The excerpt is {excerpt.Length} characters; the budget plus the marker is {maxLength + marker.Length}.");
		Assert.Equal(1, MarkerPattern().Count(excerpt));
	}

	[Fact]
	public void ToString_WhenTheBudgetIsMoreThanTwoCharacters_KeepsSomeOfTheBeginningAndSomeOfTheEnd()
	{
		string[] lines = NumberedLines(9, 100);
		OutputCapture capture = Capture(90, lines);

		string excerpt = capture.ToString();

		Match marker = MarkerPattern().Match(excerpt);
		Assert.True(marker.Index > 0, "The beginning of the output must be kept.");
		Assert.True(excerpt.Length > marker.Index + marker.Length + 1, "The end of the output must be kept.");
		Assert.StartsWith(lines[0], excerpt, StringComparison.Ordinal);
		Assert.EndsWith(lines[^1], excerpt, StringComparison.Ordinal);
	}

	[Fact]
	public void ToString_WhenTheOutputIsOneCharacterOverTheBudget_OmitsExactlyOneCharacter()
	{
		string[] lines = NumberedLines(9, 10);
		OutputCapture capture = Capture(99, lines);

		string excerpt = capture.ToString();

		Match marker = MarkerPattern().Match(excerpt);
		Assert.True(marker.Success, "One character over the budget must still be reported.");
		Assert.Equal("1", marker.Groups[1].Value);
	}

	[Fact]
	public void ToString_WithAZeroBudget_KeepsNothingButStillCountsTheOutput()
	{
		OutputCapture capture = Capture(0, ["abc", "de", "f"]);

		Assert.Equal("[truncated: 9 characters omitted]", capture.ToString());
	}

	[Fact]
	public void ToString_AfterMoreLinesArrive_KeepsTheSameBeginningAndAnEverLaterEnd()
	{
		OutputCapture capture = new(60, log: null, logProblem: null);
		string[] lines = NumberedLines(9, 50);

		for (int index = 0; index < 20; index++)
		{
			capture.Add(lines[index]);
		}

		string early = capture.ToString();

		for (int index = 20; index < 50; index++)
		{
			capture.Add(lines[index]);
		}

		string late = capture.ToString();

		string earlyHead = early[..MarkerPattern().Match(early).Index];
		string lateHead = late[..MarkerPattern().Match(late).Index];
		Assert.Equal(earlyHead, lateHead);
		Assert.EndsWith(lines[^1], late, StringComparison.Ordinal);
		Assert.DoesNotContain(lines[19], late[lateHead.Length..], StringComparison.Ordinal);
	}

	[Fact]
	public void Add_WritesEveryLineToTheLogEvenWhenTheExcerptIsCut()
	{
		using StringWriter log = new();
		OutputCapture capture = new(50, log, logProblem: null);
		string[] lines = NumberedLines(9, 1000);

		foreach (string line in lines)
		{
			capture.Add(line);
		}

		Assert.Contains("[truncated:", capture.ToString(), StringComparison.Ordinal);
		Assert.Equal(lines, log.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
	}

	[Fact]
	public void Add_FromSeveralThreadsAtOnce_KeepsWholeLinesAnAccurateCountAndALogOfEveryLine()
	{
		const int Threads = 8;
		const int LinesPerThread = 2000;
		const int Budget = 10_000;
		using StringWriter log = new();
		OutputCapture capture = new(Budget, log, logProblem: null);

		Parallel.For(0, Threads, thread =>
		{
			for (int line = 0; line < LinesPerThread; line++)
			{
				capture.Add(string.Create(CultureInfo.InvariantCulture, $"thread{thread}-line{line:D6}-padding"));
			}
		});

		string excerpt = capture.ToString();
		Match marker = MarkerPattern().Match(excerpt);
		string head = excerpt[..marker.Index];
		string tail = excerpt[(marker.Index + marker.Length)..];
		long total = (long)Threads * LinesPerThread * "thread0-line000000-padding\n".Length;
		Assert.True(marker.Success, "The output is far over the budget, so the excerpt must be cut.");
		Assert.Equal(total - Budget, long.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture));
		Assert.Equal(Budget, head.Length + tail.Length + 1);

		// Every line that was kept whole is a line that was added whole: threads never interleave inside a line.
		Regex wholeLine = new(@"^thread\d-line\d{6}-padding$");
		string[] keptHead = head.Split('\n');
		string[] keptTail = tail.Split('\n');
		Assert.All(keptHead[..^1], line => Assert.Matches(wholeLine, line));
		Assert.All(keptTail[1..], line => Assert.Matches(wholeLine, line));

		string[] logged = log.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
		Assert.Equal(Threads * LinesPerThread, logged.Length);
		Assert.All(logged, line => Assert.Matches(wholeLine, line));
		Assert.Equal(Threads * LinesPerThread, logged.Distinct().Count());
	}

	// Lines of one fixed length that end with their position, so the cut can be located exactly: "xxxxxx001" is nine characters.
	private static string[] NumberedLines(int lineLength, int lineCount)
	{
		return [.. Enumerable.Range(1, lineCount).Select(number => number.ToString(CultureInfo.InvariantCulture).PadLeft(lineLength, 'x')[^lineLength..])];
	}

	private static OutputCapture Capture(int maxLength, IEnumerable<string> lines)
	{
		OutputCapture capture = new(maxLength, log: null, logProblem: null);
		foreach (string line in lines)
		{
			capture.Add(line);
		}

		return capture;
	}
}
