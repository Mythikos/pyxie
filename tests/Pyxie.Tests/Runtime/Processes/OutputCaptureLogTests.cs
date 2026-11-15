using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>The round log is best effort. <see cref="OutputCapture"/> adds lines from the threads that read a process's output streams, where an exception would end the whole process, so a log write that fails stops the logging and puts a note at the start of the excerpt, and the log stops at its size limit.</summary>
public sealed class OutputCaptureLogTests
{
	// The most characters one round's log keeps; the tests check the behavior at this size and do not depend on how the limit is stored.
	private const long LogLimit = 50_000_000;
	private const string StopLine = "[The log stops here: it reached its size limit.]";

	[Fact]
	public void Add_WhenTheFirstLogWriteFails_DoesNotThrowAndNotesItAtTheStartOfTheExcerpt()
	{
		RecordingLogWriter log = new(failOnWrite: 1, () => new IOException("There is not enough space on the disk."));
		OutputCapture capture = new(1000, log, logProblem: null);

		capture.Add("first");
		capture.Add("second");

		Assert.Equal("[The round log could not be written: There is not enough space on the disk.]\nfirst\nsecond", capture.ToString());
	}

	[Theory]
	[InlineData("io")]
	[InlineData("access")]
	[InlineData("disposed")]
	public void Add_WhenTheLogWriteFailsWithAnExpectedException_DoesNotThrow(string failure)
	{
		RecordingLogWriter log = new(
			failOnWrite: 1,
			() => failure switch
			{
				"io" => new IOException("the log is locked"),
				"access" => new UnauthorizedAccessException("the log is read-only"),
				_ => new ObjectDisposedException("log"),
			});
		OutputCapture capture = new(1000, log, logProblem: null);

		Exception? thrown = Record.Exception(() => capture.Add("a line"));

		Assert.Null(thrown);
		Assert.StartsWith("[The round log could not be written: ", capture.ToString(), StringComparison.Ordinal);
		Assert.EndsWith("a line", capture.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Add_AfterALogWriteFailed_NeverWritesToTheLogAgainButKeepsEveryLineInTheExcerpt()
	{
		RecordingLogWriter log = new(failOnWrite: 2);
		OutputCapture capture = new(1000, log, logProblem: null);

		capture.Add("one");
		capture.Add("two");
		capture.Add("three");
		capture.Add("four");

		Assert.Equal(["one"], log.Lines);
		Assert.Equal(2, log.Attempts);
		Assert.Equal("[The round log could not be written: There is not enough space on the disk.]\none\ntwo\nthree\nfour", capture.ToString());
	}

	[Fact]
	public void ToString_WhenTheLogNeverFailed_HasNoNote()
	{
		RecordingLogWriter log = new();
		OutputCapture capture = new(1000, log, logProblem: null);

		capture.Add("one");
		capture.Add("two");

		Assert.Equal(["one", "two"], log.Lines);
		Assert.Equal("one\ntwo", capture.ToString());
	}

	[Fact]
	public void Add_WhenTheOutputIsCutAndTheLogFailed_KeepsTheNoteBeforeTheBeginningAndTheMarkerAfterIt()
	{
		RecordingLogWriter log = new(failOnWrite: 1);
		OutputCapture capture = new(30, log, logProblem: null);

		for (int line = 0; line < 40; line++)
		{
			capture.Add($"line {line:00}");
		}

		string excerpt = capture.ToString();
		Assert.StartsWith("[The round log could not be written: ", excerpt, StringComparison.Ordinal);
		Assert.Contains("[truncated: ", excerpt, StringComparison.Ordinal);
		Assert.EndsWith("line 39", excerpt, StringComparison.Ordinal);
	}

	[Fact]
	public void Add_WhenTheLogReachesItsSizeLimit_WritesAStopLineAndNothingAfterIt()
	{
		RecordingLogWriter log = new(keepLines: false);
		OutputCapture capture = new(1000, log, logProblem: null);
		string megabyte = new('x', 1_000_000);

		for (int line = 0; line < 60; line++)
		{
			capture.Add(megabyte);
		}

		capture.Add("a line after the stop");

		Assert.Equal(StopLine, log.LastLine);
		Assert.True(log.Characters <= LogLimit + StopLine.Length, $"The log took {log.Characters} characters; its limit is {LogLimit}.");
		Assert.True(log.Characters > LogLimit - (2 * megabyte.Length), $"The log stopped far below its limit, at {log.Characters} characters.");
		Assert.Equal(log.Attempts, log.WrittenLines);
		Assert.DoesNotContain("could not be written", capture.ToString(), StringComparison.Ordinal);
		Assert.EndsWith("a line after the stop", capture.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Add_AfterTheLogReachedItsSizeLimit_NeverWritesToTheLogAgain()
	{
		RecordingLogWriter log = new(keepLines: false);
		OutputCapture capture = new(1000, log, logProblem: null);
		string megabyte = new('x', 1_000_000);
		for (int line = 0; line < 60; line++)
		{
			capture.Add(megabyte);
		}

		int attemptsAtTheStop = log.Attempts;
		capture.Add("one more");
		capture.Add("and another");

		Assert.Equal(attemptsAtTheStop, log.Attempts);
	}
}
