using System.Diagnostics;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Gates;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Gates;

/// <summary><see cref="GateRunner"/> when a gate goes wrong in the sandbox: a gate that exits with a non-zero code or times out fails the round with its output (the worker gets it), and the gates after it do not run. A gate that cannot start is a configuration problem for a person, so it blocks. A gate stopped part-way spoils the sandbox.</summary>
public sealed class GateRunnerFailureTests
{
	// Timers fire sixty times sooner, so a gate with a one-minute timeout is stopped after a second.
	private const int TimerAcceleration = 60;
	private static readonly TimeSpan s_slowestAcceptableStop = TimeSpan.FromSeconds(30);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_StopsAtTheFirstFailingGateAndDoesNotRunTheRest()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build"), rig.CreateGate("Test", ["--exit", "3"]), rig.CreateGate("Lint"));

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal(["Build", "Test"], (await rig.ReadStartedAsync()).Select(run => run.Gate));
		Assert.False(rig.Sandbox.IsSpoiled, "A gate that failed by itself leaves nothing running in the sandbox.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAFailingGate_NamesItAndItsExitCodeInTheNote()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Test", ["--exit", "3"]));

		Assert.Equal("Gate 'Test' failed with exit code 3.", verdict.Note);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAFailingGate_ListsThePassedGatesAndQuotesOnlyTheFailingGatesOutput()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build", ["--outcome", "output-of-build"]), rig.CreateGate("Test", ["--stderr", "error-of-test", "--outcome", "output-of-test", "--exit", "3"]), rig.CreateGate("Lint", ["--outcome", "output-of-lint"]));

		string report = verdict.Report ?? string.Empty;
		Assert.Contains("- Build: passed in ", report, StringComparison.Ordinal);
		Assert.Contains("- Test: failed with exit code 3 in ", report, StringComparison.Ordinal);
		Assert.Contains("## Test output", report, StringComparison.Ordinal);
		Assert.Contains("error-of-test", report, StringComparison.Ordinal);
		Assert.Contains("fake harness: output-of-test", report, StringComparison.Ordinal);
		Assert.DoesNotContain("output-of-build", report, StringComparison.Ordinal);
		Assert.DoesNotContain("Lint", report, StringComparison.Ordinal);
		Assert.True(report.IndexOf("- Test: failed", StringComparison.Ordinal) < report.IndexOf("## Test output", StringComparison.Ordinal), "The summary comes before the quoted output.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAFailingGateThatPrintedNothing_SaysSoInsteadOfQuotingNothing()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(await rig.CreateSilentFailingGateAsync("Check"));

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal("Gate 'Check' failed with exit code 1.", verdict.Note);
		Assert.NotNull(verdict.Report);
		Assert.EndsWith("## Check output\n\n(no output)", verdict.Report, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_FailsAGateThatTimesOutDoesNotRunTheRestAndSpoilsTheSandbox()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync(new AcceleratedTimeProvider(TimerAcceleration));
		Stopwatch stopwatch = Stopwatch.StartNew();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build"), rig.CreateGate("Slow", ["--sleep", "120000"], timeoutMinutes: 1), rig.CreateGate("Lint"));

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal("Gate 'Slow' timed out after 1 minute.", verdict.Note);
		Assert.Contains("- Build: passed in ", verdict.Report, StringComparison.Ordinal);
		Assert.Contains("- Slow: timed out after 1 minute", verdict.Report, StringComparison.Ordinal);
		Assert.Contains("## Slow output", verdict.Report, StringComparison.Ordinal);
		Assert.Equal(["Build", "Slow"], (await rig.ReadStartedAsync()).Select(run => run.Gate));
		Assert.True(stopwatch.Elapsed < s_slowestAcceptableStop, $"The round took {stopwatch.Elapsed}: the gate should have been stopped at its timeout, not waited for.");
		Assert.True(rig.Sandbox.IsSpoiled, "A gate stopped at its timeout may have left processes behind, so the sandbox must not be used again.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_BlocksWhenAGateCannotStartAndDoesNotRunTheRest()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();
		Gate broken = GateRunnerRig.CreateGateThatCannotStart("Broken");

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build"), broken, rig.CreateGate("Lint"));

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.Equal($"Gate 'Broken' could not start: The command {broken.Command} was not found.", verdict.Note);
		Assert.Contains("- Build: passed in ", verdict.Report, StringComparison.Ordinal);
		Assert.Contains("- Broken: could not start", verdict.Report, StringComparison.Ordinal);
		Assert.Equal(["Build"], (await rig.ReadStartedAsync()).Select(run => run.Gate));
		string log = await File.ReadAllTextAsync(rig.LogPath, Token);
		Assert.Contains($"== Broken: {broken.Command}", log, StringComparison.Ordinal);
		Assert.DoesNotContain("== Lint:", log, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenCancelled_StopsTheRunningGateThrowsAndSpoilsTheSandbox()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		Stopwatch stopwatch = Stopwatch.StartNew();

		Task<RoundVerdict> running = rig.RunAsync([rig.CreateGate("Slow", ["--sleep", "120000"]), rig.CreateGate("Lint")], cancellation.Token);
		await rig.WaitForFirstStartAsync();
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
		Assert.Equal(["Slow"], (await rig.ReadStartedAsync()).Select(run => run.Gate));
		Assert.True(stopwatch.Elapsed < s_slowestAcceptableStop, $"Cancelling took {stopwatch.Elapsed}: the running gate should have been killed, not waited for.");
		Assert.True(rig.Sandbox.IsSpoiled, "A gate stopped part-way spoils the sandbox.");
	}
}
