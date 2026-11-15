using System.Globalization;

using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> and output: both streams are captured, standard output is also kept alone, the excerpts are bounded, and the log file receives every line.</summary>
public sealed class ProcessRunnerOutputTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_CapturesStandardOutput()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--outcome", "completed"), Token).WithinLimitAsync();

		Assert.Equal("fake harness: completed", result.Output);
	}

	[Fact]
	public async Task RunAsync_CapturesStandardError()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);

		// The fake harness complains on standard error, and exits with 64, when its input is not the prompt it was told to require.
		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--require-prompt", "the prompt") with { StandardInput = "something else" }, Token).WithinLimitAsync();

		Assert.Equal(64, result.ExitCode);
		Assert.Contains("Expected the prompt on standard input; got: something else", result.Output, StringComparison.Ordinal);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_CapturesBothStreamsOfOneProcess()
	{
		await using ScratchFolder scratch = new();
		string script = await BatchScript.WriteAsync(scratch.Folder, "both.cmd", "@echo to standard output", "@echo to standard error 1>&2");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(script, [], scratch.Folder), Token).WithinLimitAsync();

		Assert.Contains("to standard output", result.Output, StringComparison.Ordinal);
		Assert.Contains("to standard error", result.Output, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_KeepsStandardOutputAloneInStandardOutputAndBothStreamsInOutputEachInItsOwnOrder()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(scratch.Folder, "--stdout", "out one", "--stderr", "err two", "--stdout", "out three", "--stderr", "err four");

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Equal("out one\nout three\nfake harness: completed", result.StandardOutput.ReplaceLineEndings("\n"));

		// The two streams are separate pipes read side by side, so how their lines interleave depends on which is read first; each stream keeps its own order.
		string[] lines = result.Output.ReplaceLineEndings("\n").Split('\n');
		string[] standardOutput = ["out one", "out three", "fake harness: completed"];
		string[] standardError = ["err two", "err four"];
		Assert.Equal(standardOutput, lines.Where(line => !line.StartsWith("err ", StringComparison.Ordinal)));
		Assert.Equal(standardError, lines.Where(line => line.StartsWith("err ", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task RunAsync_ForAProcessThatOnlyWritesStandardError_LeavesStandardOutputEmptyAndOutputWithTheError()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--require-prompt", "the prompt") with { StandardInput = "something else" }, Token).WithinLimitAsync();

		Assert.Equal(string.Empty, result.StandardOutput);
		Assert.Contains("Expected the prompt on standard input", result.Output, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_ShortensStandardOutputTheSameWayAsTheCombinedOutput()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(scratch.Folder, "--outcome", Lines(1, 2000)) with { MaxOutputLength = 1000 };

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Contains("[truncated:", result.StandardOutput, StringComparison.Ordinal);
		Assert.StartsWith("fake harness: line 0001", result.StandardOutput, StringComparison.Ordinal);
		Assert.EndsWith("line 2000", result.StandardOutput, StringComparison.Ordinal);
		Assert.Equal(result.Output, result.StandardOutput);
	}

	[Fact]
	public async Task RunAsync_KeepsTheLinesOfAnOutputStreamInOrder()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--outcome", Lines(1, 50)), Token).WithinLimitAsync();

		Assert.Equal($"fake harness: {Lines(1, 50)}", result.Output.ReplaceLineEndings("\n"));
	}

	[Fact]
	public async Task RunAsync_KeepsTheStartAndTheEndOfOutputLongerThanTheLimit()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(scratch.Folder, "--outcome", Lines(1, 2000)) with { MaxOutputLength = 1000 };

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Contains("[truncated:", result.Output, StringComparison.Ordinal);
		Assert.StartsWith("fake harness: line 0001", result.Output, StringComparison.Ordinal);
		Assert.EndsWith("line 2000", result.Output, StringComparison.Ordinal);
		Assert.True(result.Output.Length <= 1000 + "[truncated: 99999 characters omitted]\n".Length, $"The excerpt is {result.Output.Length} characters; the limit was 1000 plus the marker.");
	}

	[Fact]
	public async Task RunAsync_WritesEveryLineToTheLogFileEvenWhenTheExcerptIsCut()
	{
		await using ScratchFolder scratch = new();
		string log = scratch.Combine(Path.Combine("logs", "deeper", "run.log"));
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(scratch.Folder, "--outcome", Lines(1, 2000)) with { MaxOutputLength = 1000, LogPath = log };

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Contains("[truncated:", result.Output, StringComparison.Ordinal);
		string[] logged = (await File.ReadAllTextAsync(log, Token)).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
		Assert.Equal(2000, logged.Length);
		Assert.Equal("fake harness: line 0001", logged[0]);
		Assert.Equal(Enumerable.Range(2, 1999).Select(Line), logged[1..]);
	}

	[Fact]
	public async Task RunAsync_AppendsToAnExistingLogFile()
	{
		await using ScratchFolder scratch = new();
		string log = scratch.CreateFile("run.log", "from an earlier run\n");
		using ProcessRunner runner = new(TimeProvider.System);

		await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--outcome", "again") with { LogPath = log }, Token).WithinLimitAsync();

		Assert.Equal("from an earlier run\nfake harness: again\n", (await File.ReadAllTextAsync(log, Token)).ReplaceLineEndings("\n"));
	}

	// Lines of ten characters each ("line 0001" and a line break), so two thousand of them are about twenty thousand characters, which still fits on a command line.
	private static string Lines(int first, int last)
	{
		return string.Join('\n', Enumerable.Range(first, last - first + 1).Select(Line));
	}

	private static string Line(int number)
	{
		return string.Create(CultureInfo.InvariantCulture, $"line {number:D4}");
	}
}
