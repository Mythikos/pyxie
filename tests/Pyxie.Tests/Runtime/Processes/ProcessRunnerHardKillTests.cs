using System.Diagnostics;

using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>The job object ends a runner's processes even when Pyxie itself is killed and cannot clean up. The test starts a second copy of the test program that runs one explicit test (<see cref="HostedRunnerThatNeverEnds"/>) which starts a long-running process through a <see cref="ProcessRunner"/>, then kills that host hard (no <c>Dispose</c>, no finalizers) and checks that the process it started is gone. Only the host is killed, never its tree: the process must be ended by the job and not by the kill.</summary>
public sealed class ProcessRunnerHardKillTests
{
	private const string HostFolderVariable = "PYXIE_TEST_JOB_HOST_FOLDER";
	private static readonly TimeSpan s_hostLimit = TimeSpan.FromSeconds(90);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task KillingTheProcessThatRunsTheRunner_EndsTheProcessesItStarted()
	{
		await using ScratchFolder scratch = new();
		string work = scratch.CreateSubfolder("work");
		string ready = scratch.Combine("ready.txt");
		ProcessStartInfo startInfo = new(Path.Combine(AppContext.BaseDirectory, "Pyxie.Tests.exe"))
		{
			WorkingDirectory = scratch.Folder,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		foreach (string argument in new[] { "-method", $"{typeof(ProcessRunnerHardKillTests).FullName}.{nameof(HostedRunnerThatNeverEnds)}", "-explicit", "only", "-noColor" })
		{
			startInfo.ArgumentList.Add(argument);
		}

		startInfo.Environment[HostFolderVariable] = scratch.Folder;
		using Process host = Process.Start(startInfo) ?? throw new InvalidOperationException("The test program could not be started.");

		try
		{
			using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
			limit.CancelAfter(s_hostLimit);
			Task hostEnded = host.WaitForExitAsync(limit.Token);
			Task started = ChildProcessProbe.WaitForFileAsync(ready, "the hosted runner to start its process");
			Task first = await Task.WhenAny(started, hostEnded);
			Assert.True(ReferenceEquals(first, started), "The test program ended before it started the process.");
			await started;
			Assert.True(ChildProcessProbe.IsHeld(work), "The process the host started is running.");
		}
		finally
		{
			if (!host.HasExited)
			{
				host.Kill(entireProcessTree: false);
			}

			await host.WaitForExitAsync(CancellationToken.None);
		}

		await ChildProcessProbe.AssertReleasedAsync(work, "The process the killed host had started");
	}

	[Fact(Explicit = true)]
	public async Task HostedRunnerThatNeverEnds()
	{
		string folder = Environment.GetEnvironmentVariable(HostFolderVariable) ?? throw new InvalidOperationException($"{HostFolderVariable} names the folder the hosting test prepared; run ProcessRunnerHardKillTests instead.");
		string record = Path.Combine(folder, "record.txt");
		using ProcessRunner runner = new(TimeProvider.System);
		Task<ProcessResult> run = runner.RunAsync(FakeHarnessRequest.Create(Path.Combine(folder, "work"), "--record", record, "--sleep", "120000"), CancellationToken.None);
		await ChildProcessProbe.WaitForFileAsync(record, "the hosted process to start");
		await File.WriteAllTextAsync(Path.Combine(folder, "ready.txt"), string.Empty, Token);

		// The process sleeps for two minutes and the hosting test kills this program long before, so the wait ends with the kill.
		await run.WaitAsync(Token);
	}
}
