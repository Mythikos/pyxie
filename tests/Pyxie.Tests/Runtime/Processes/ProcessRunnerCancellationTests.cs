using System.Diagnostics;

using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> when the caller cancels: the process and its children are killed, and the run ends with an <see cref="OperationCanceledException"/> rather than a result.</summary>
public sealed class ProcessRunnerCancellationTests
{
	private static readonly TimeSpan s_quickly = TimeSpan.FromSeconds(30);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_WhenCancelledWhileTheProcessRuns_ThrowsQuicklyAndKillsIt()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");
		using ProcessRunner runner = new(TimeProvider.System);
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

		Task<ProcessResult> run = runner.RunAsync(FakeHarnessRequest.Create(work, "--record", record, "--sleep", "120000"), cancellation.Token);
		await ChildProcessProbe.WaitForFileAsync(record, "the process to start");
		Assert.True(ChildProcessProbe.IsHeld(work) || !OperatingSystem.IsWindows(), "A running process keeps its working directory from being deleted, which is how the test tells later that it is gone.");
		Stopwatch stopwatch = Stopwatch.StartNew();

		await cancellation.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WithinLimitAsync());

		Assert.True(stopwatch.Elapsed < s_quickly, $"Cancelling took {stopwatch.Elapsed}; a process that sleeps for two minutes must be killed, not waited for.");
		await ChildProcessProbe.AssertReleasedAsync(work, "The cancelled process");
	}

	[Fact]
	public async Task RunAsync_WhenCancelledBeforeItStarts_ThrowsWithoutStartingTheProcess()
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string log = scratch.Combine("run.log");
		using ProcessRunner runner = new(TimeProvider.System);
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(FakeHarnessRequest.Create(scratch.Folder, "--record", record) with { LogPath = log }, cancellation.Token));

		Assert.False(File.Exists(record), "The process must not start once the run is cancelled.");
		Assert.False(File.Exists(log), "A run cancelled before it started must not even open its log.");
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_WhenCancelled_KillsTheProcessesItStartedToo()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");
		string script = await BatchScript.WriteAsync(scratch.Folder, "starts-a-child.cmd", $"@dotnet \"{FakeHarnessAgent.DllPath}\" --record \"{record}\" --sleep 120000");
		using ProcessRunner runner = new(TimeProvider.System);
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

		Task<ProcessResult> run = runner.RunAsync(new ProcessRequest(script, [], work), cancellation.Token);
		await ChildProcessProbe.WaitForFileAsync(record, "the batch file's child process to start");
		await cancellation.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WithinLimitAsync());

		await ChildProcessProbe.AssertReleasedAsync(work, "The child process of the batch file");
	}
}
