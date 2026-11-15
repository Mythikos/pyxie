using System.Diagnostics;

using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> when a process outlives its timeout: it is killed with every process it started and the run is a <see cref="ProcessOutcome.TimedOut"/> result. The tests drive a <see cref="ManualTimeProvider"/>, so the timeout fires when the test says, after the process is known to be running. That a process is gone is checked through its working directory (see <see cref="ChildProcessProbe"/>), which a live process, or a live child of it, keeps from being deleted.</summary>
public sealed class ProcessRunnerTimeoutTests
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(7);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_WhenTheProcessRunsPastItsTimeout_KillsItAndReportsTimedOut()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");
		ManualTimeProvider clock = new();
		using ProcessRunner runner = new(clock);
		ProcessRequest request = FakeHarnessRequest.Create(work, "--record", record, "--sleep", "120000") with { Timeout = s_timeout };

		Task<ProcessResult> run = runner.RunAsync(request, Token);
		await clock.TimerScheduled.WithinLimitAsync();
		await ChildProcessProbe.WaitForFileAsync(record, "the process to start");
		Assert.False(run.IsCompleted, "The process sleeps for two minutes, so the run cannot end before the timeout.");
		Assert.True(ChildProcessProbe.IsHeld(work) || !OperatingSystem.IsWindows(), "A running process keeps its working directory from being deleted, which is how the test tells later that it is gone.");

		clock.Advance(s_timeout);
		ProcessResult result = await run.WithinLimitAsync();

		Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
		Assert.Equal(-1, result.ExitCode);
		Assert.Equal(s_timeout, result.Duration);
		Assert.False(result.Succeeded, "A process that timed out did not succeed.");
		Assert.Null(result.StartError);
		await ChildProcessProbe.AssertReleasedAsync(work, "The process that timed out");
	}

	[Fact]
	public async Task RunAsync_WithTheSystemClock_TimesOutAfterTheRequestedTime()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = FakeHarnessRequest.Create(work, "--sleep", "120000") with { Timeout = TimeSpan.FromSeconds(1) };
		Stopwatch stopwatch = Stopwatch.StartNew();

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
		Assert.True(result.Duration >= TimeSpan.FromSeconds(0.9), $"The run reports {result.Duration}, which is well under its one second timeout.");
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"The run took {stopwatch.Elapsed} to stop a process that sleeps for two minutes.");
		await ChildProcessProbe.AssertReleasedAsync(work, "The process that timed out");
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_WhenAProcessTimesOut_KillsTheProcessesItStartedAndKeepsItsOutputSoFar()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");

		// The batch file's cmd.exe starts the sleeping fake harness as its child; both inherit the working directory.
		string script = await BatchScript.WriteAsync(scratch.Folder, "starts-a-child.cmd", "@echo started", $"@dotnet \"{FakeHarnessAgent.DllPath}\" --record \"{record}\" --sleep 120000");
		ManualTimeProvider clock = new();
		using ProcessRunner runner = new(clock);

		Task<ProcessResult> run = runner.RunAsync(new ProcessRequest(script, [], work) { Timeout = s_timeout }, Token);
		await clock.TimerScheduled.WithinLimitAsync();
		await ChildProcessProbe.WaitForFileAsync(record, "the batch file's child process to start");
		clock.Advance(s_timeout);
		ProcessResult result = await run.WithinLimitAsync();

		Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
		Assert.Contains("started", result.Output, StringComparison.Ordinal);
		await ChildProcessProbe.AssertReleasedAsync(work, "The child process of the batch file");
	}
}
