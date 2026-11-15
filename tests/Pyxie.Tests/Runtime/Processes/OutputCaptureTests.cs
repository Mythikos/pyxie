using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="OutputCapture"/> when the output fits, plus the log and the seal. What it keeps when the output does not fit is in <see cref="OutputCaptureTruncationTests"/>.</summary>
public sealed class OutputCaptureTests
{
	[Fact]
	public void ToString_BeforeAnyLine_IsEmpty()
	{
		OutputCapture capture = new(100, log: null, logProblem: null);

		Assert.Equal(string.Empty, capture.ToString());
	}

	[Fact]
	public void ToString_WhenEverythingFits_ReturnsTheLinesWithoutAMarkerOrATrailingLineBreak()
	{
		OutputCapture capture = new(100, log: null, logProblem: null);

		capture.Add("first");
		capture.Add("second");
		capture.Add("third");

		Assert.Equal("first\nsecond\nthird", capture.ToString());
	}

	[Fact]
	public void ToString_KeepsBlankLinesBetweenOtherLines()
	{
		OutputCapture capture = new(100, log: null, logProblem: null);

		capture.Add("above");
		capture.Add(string.Empty);
		capture.Add("below");

		Assert.Equal("above\n\nbelow", capture.ToString());
	}

	[Fact]
	public void ToString_WhenTheOutputIsExactlyTheBudget_KeepsAllOfIt()
	{
		OutputCapture capture = new(100, log: null, logProblem: null);

		for (int line = 0; line < 10; line++)
		{
			capture.Add("123456789");
		}

		string expected = string.Join('\n', Enumerable.Repeat("123456789", 10));
		Assert.Equal(expected, capture.ToString());
	}

	[Fact]
	public void Add_WritesEveryLineToTheLogInFull()
	{
		using StringWriter log = new();
		OutputCapture capture = new(100, log, logProblem: null);

		capture.Add("first");
		capture.Add(string.Empty);
		capture.Add("third");

		Assert.Equal("first\n\nthird\n", log.ToString().ReplaceLineEndings("\n"));
	}

	[Fact]
	public void Add_WithoutALog_Works()
	{
		OutputCapture capture = new(10, log: null, logProblem: null);

		capture.Add("a line that is longer than the budget");

		Assert.Contains("[truncated:", capture.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Add_AfterSeal_IsIgnoredByTheExcerptAndTheLog()
	{
		using StringWriter log = new();
		OutputCapture capture = new(100, log, logProblem: null);
		capture.Add("before");

		capture.Seal();
		capture.Add("after");

		Assert.Equal("before", capture.ToString());
		Assert.Equal("before\n", log.ToString().ReplaceLineEndings("\n"));
	}

	[Fact]
	public void Add_AfterSealAndAfterTheLogIsDisposed_DoesNotTouchTheLog()
	{
		StringWriter log = new();
		OutputCapture capture = new(100, log, logProblem: null);
		capture.Add("while running");
		capture.Seal();
		log.Dispose();

		capture.Add("from a process that outlived the run");

		Assert.Equal("while running", capture.ToString());
	}
}
