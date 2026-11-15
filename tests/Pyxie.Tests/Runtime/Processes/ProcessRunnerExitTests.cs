using System.Diagnostics;
using System.Globalization;

using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> when a process runs to its end by itself: its exit code, where it runs, and the arguments it receives.</summary>
public sealed class ProcessRunnerExitTests
{
	public static TheoryData<string> AwkwardArguments { get; } =
	[
		"plain",
		"two words",
		"",
		"say \"hi\" now",
		"\"already quoted\"",
		@"C:\dir with space\",
		@"trailing backslash\",
		"backslashes before a quote \\\\\"",
		"tab\tseparated",
		"line one\nline two",
		"--looks-like-an-option",
		"100% done! & more | less",
		"café ✓",
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(255)]
	public async Task RunAsync_ReportsTheExitCodeOfAProcessThatExitedByItself(int exitCode)
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--exit", exitCode.ToString(CultureInfo.InvariantCulture)), Token).WithinLimitAsync();

		Assert.Equal(ProcessOutcome.Exited, result.Outcome);
		Assert.Equal(exitCode, result.ExitCode);
		Assert.Equal(exitCode == 0, result.Succeeded);
		Assert.Null(result.StartError);
	}

	[Fact]
	public async Task RunAsync_RunsTheProcessInTheWorkingDirectory()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(work, "--record", record), Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The process did not succeed: {result.Output}");
		Assert.Equal(work, (await FakeHarnessRecord.ReadSingleAsync(record)).Directory, ignoreCase: OperatingSystem.IsWindows());
	}

	[Theory]
	[MemberData(nameof(AwkwardArguments))]
	public async Task RunAsync_PassesEachArgumentToTheProcessAsOne(string argument)
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--record", record, "--note", argument), Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The process did not succeed: {result.Output}");
		Assert.Equal(["--record", record, "--note", argument], (await FakeHarnessRecord.ReadSingleAsync(record)).Arguments);
	}

	[Fact]
	public async Task RunAsync_MeasuresTheDurationOnTheClockItWasGiven()
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(new ManualTimeProvider());

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder), Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The process did not succeed: {result.Output}");
		Assert.Equal(TimeSpan.Zero, result.Duration);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_WhenTheProcessLeavesAChildHoldingItsOutput_ReturnsWithoutWaitingForTheChild()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");

		// The child inherits the output pipes and lives for about eleven seconds, as a build server a gate started would. It runs elsewhere so it does not hold the test's folders.
		string script = await BatchScript.WriteAsync(scratch.Folder, "leaves-a-child.cmd", "@echo done", "@start \"\" /b /d \"%SystemRoot%\" ping -n 12 127.0.0.1");
		using ProcessRunner runner = new(TimeProvider.System);
		Stopwatch stopwatch = Stopwatch.StartNew();

		ProcessResult result = await runner.RunAsync(new ProcessRequest(script, [], work), Token).WithinLimitAsync();

		Assert.Equal(ProcessOutcome.Exited, result.Outcome);
		Assert.Equal(0, result.ExitCode);
		Assert.Contains("done", result.Output, StringComparison.Ordinal);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(9), $"The run took {stopwatch.Elapsed}; it waited for the child that held the output pipes instead of giving up on them after the process exited.");
	}
}
