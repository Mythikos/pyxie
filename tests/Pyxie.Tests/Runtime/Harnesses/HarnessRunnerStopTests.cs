using System.Diagnostics;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> when the agent is stopped: a timeout is an error worth another attempt, and cancellation (the pipeline stopping) ends the round with an exception. Either way the agent is killed, which the tests check through its working directory (see <see cref="ChildProcessProbe"/>), and the sandbox is spoiled, because what the agent left running cannot be trusted to stop. The timeout of the first test is driven by a <see cref="ManualTimeProvider"/>.</summary>
public sealed class HarnessRunnerStopTests
{
	private static readonly TimeSpan s_quickly = TimeSpan.FromSeconds(60);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AnAgentThatRunsPastItsTimeout_IsErroredKilledAndSpoilsTheSandbox()
	{
		ManualTimeProvider clock = new();
		await using HarnessRig rig = await HarnessRig.CreateAsync(clock);
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Sleeper", HarnessKind.Other, "--record", record, "--sleep", "120000");
		TimeSpan timeout = TimeSpan.FromMinutes(1);

		Task<RoundVerdict> run = rig.StartAsync(HarnessRig.Work(agent), timeout, Token);
		await clock.TimerScheduled.WithinLimitAsync();
		await ChildProcessProbe.WaitForFileAsync(record, "the agent to start");
		Assert.True(ChildProcessProbe.IsHeld(rig.RepositoryFolder) || !OperatingSystem.IsWindows(), "A running agent keeps its repository folder from being deleted, which is how the test tells later that it is gone.");

		clock.Advance(timeout);
		RoundVerdict verdict = await run.WithinLimitAsync();

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal("Agent 'Sleeper' was stopped after running for 1 minute.", verdict.Note);
		Assert.NotNull(verdict.Report);
		Assert.True(rig.Sandbox.IsSpoiled, "An agent stopped at its timeout may have left processes behind, so the sandbox must not be used again.");
		await ChildProcessProbe.AssertReleasedAsync(rig.RepositoryFolder, "The agent that timed out");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithTheSystemClock_StopsAnAgentThatSleepsPastItsTimeoutQuickly()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Sleeper", HarnessKind.Other, "--sleep", "120000");
		Stopwatch stopwatch = Stopwatch.StartNew();

		RoundVerdict verdict = await rig.StartAsync(HarnessRig.Work(agent), TimeSpan.FromSeconds(1), Token).WithinLimitAsync();

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal("Agent 'Sleeper' was stopped after running for 1.0s.", verdict.Note);
		Assert.True(stopwatch.Elapsed < s_quickly, $"Stopping the agent took {stopwatch.Elapsed}; an agent that sleeps for two minutes must be killed at its timeout, not waited for.");
		Assert.True(rig.Sandbox.IsSpoiled, "An agent stopped at its timeout spoils the sandbox.");
		await ChildProcessProbe.AssertReleasedAsync(rig.RepositoryFolder, "The agent that timed out");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenCancelledWhileTheAgentRuns_ThrowsKillsTheAgentAndSpoilsTheSandbox()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Sleeper", HarnessKind.Other, "--record", record, "--sleep", "120000");
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

		Task<RoundVerdict> run = rig.StartAsync(HarnessRig.Work(agent), TimeSpan.FromMinutes(5), cancellation.Token);
		await ChildProcessProbe.WaitForFileAsync(record, "the agent to start");
		Stopwatch stopwatch = Stopwatch.StartNew();

		await cancellation.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WithinLimitAsync());

		Assert.True(stopwatch.Elapsed < s_quickly, $"Cancelling took {stopwatch.Elapsed}; the agent must be killed, not waited for.");
		Assert.True(rig.Sandbox.IsSpoiled, "An agent stopped part-way by cancellation spoils the sandbox.");
		await ChildProcessProbe.AssertReleasedAsync(rig.RepositoryFolder, "The cancelled agent");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenCancelledBeforeItStarts_ThrowsWithoutStartingTheAgent()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--record", record);
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.StartAsync(HarnessRig.Work(agent), TimeSpan.FromMinutes(1), cancellation.Token));

		Assert.False(File.Exists(record), "The agent must not start once the round is cancelled.");
	}
}
