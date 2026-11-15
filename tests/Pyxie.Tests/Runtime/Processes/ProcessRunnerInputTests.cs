using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> and standard input: what is written arrives complete, and the stream is closed so a process that reads to its end finishes.</summary>
public sealed class ProcessRunnerInputTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_DeliversStandardInputAndClosesIt()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(scratch.Folder, "--require-prompt", "the prompt") with { StandardInput = "the prompt" };

		// The fake harness reads its input to the end, so it only gets to compare it once the runner has closed the stream.
		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The process did not receive the input and exit: {result.Outcome}, exit code {result.ExitCode}, {result.Output}");
	}

	[Fact]
	public async Task RunAsync_WithoutInput_ClosesStandardInputAtOnce()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(scratch.Folder, "--require-prompt", string.Empty);

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"A process that reads its input to the end must see it end at once when there is none: {result.Outcome}, exit code {result.ExitCode}, {result.Output}");
	}

	[Fact]
	public async Task RunAsync_DeliversTheInputExactly()
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string input = "first line\r\nsecond line\n  indented  \n\nlast line without a line break";
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--record", record) with { StandardInput = input }, Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The process did not succeed: {result.Output}");
		Assert.Equal(input, (await FakeHarnessRecord.ReadSingleAsync(record)).Input);
	}

	[Fact]
	public async Task RunAsync_DeliversInputLargerThanAPipeBufferInFull()
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string input = string.Join('\n', Enumerable.Range(0, 10_000).Select(line => $"line {line:D5} {new string('x', 85)}"));
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--record", record) with { StandardInput = input }, Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The process did not succeed: {result.Output}");
		string received = (await FakeHarnessRecord.ReadSingleAsync(record)).Input;
		Assert.True(received == input, $"The process received {received.Length} characters of the {input.Length} written.");
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_WhenTheProcessExitsWithoutReadingItsInput_ReportsItsExitCode()
	{
		await using ScratchFolder scratch = new();
		string script = await BatchScript.WriteAsync(scratch.Folder, "ignores-input.cmd", "@exit /b 3");
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = new(script, [], scratch.Folder) { StandardInput = new string('x', 5_000_000) };

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Equal(ProcessOutcome.Exited, result.Outcome);
		Assert.Equal(3, result.ExitCode);
	}
}
