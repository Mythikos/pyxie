using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>On Windows every process the runner starts joins a job object that ends with it, so an agent or gate never outlives Pyxie. The batch file starts a child that is not waited for and ends at once: the child holds its working directory open, which is how the test sees it alive, and it is not killed when the run ends, only when the runner is disposed.</summary>
public sealed class ProcessRunnerJobTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Dispose_EndsAChildThatTheStartedProcessLeftRunningAfterItExited()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");
		string script = await BatchScript.WriteAsync(scratch.Folder, "leaves-a-child.cmd", $"@start \"\" /b dotnet \"{FakeHarnessAgent.DllPath}\" --record \"{record}\" --sleep 120000");
		ProcessRunner runner = new(TimeProvider.System);

		try
		{
			ProcessResult result = await runner.RunAsync(new ProcessRequest(script, [], work), Token).WithinLimitAsync();
			await ChildProcessProbe.WaitForFileAsync(record, "the batch file's child process to start");

			Assert.True(result.Succeeded, $"The batch file itself ends at once: {result.Output}");
			Assert.True(ChildProcessProbe.IsHeld(work), "The child keeps running after the process that started it ended, which is what the job is for.");
		}
		finally
		{
			runner.Dispose();
		}

		await ChildProcessProbe.AssertReleasedAsync(work, "The child that the batch file left running");
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Dispose_EndsAProcessThatIsStillRunning()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string record = scratch.Combine("record.txt");
		ProcessRunner runner = new(TimeProvider.System);
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		Task<ProcessResult> run = runner.RunAsync(FakeHarnessRequest.Create(work, "--record", record, "--sleep", "120000"), cancellation.Token);

		try
		{
			await ChildProcessProbe.WaitForFileAsync(record, "the process to start");
			Assert.True(ChildProcessProbe.IsHeld(work), "A running process keeps its working directory from being deleted.");
		}
		finally
		{
			runner.Dispose();
		}

		await ChildProcessProbe.AssertReleasedAsync(work, "The process that was still running when the runner was disposed");
		await cancellation.CancelAsync();
		await Record.ExceptionAsync(() => run.WithinLimitAsync());
	}
}
