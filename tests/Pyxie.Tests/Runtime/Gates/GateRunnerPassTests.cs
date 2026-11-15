using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Gates;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Gates;

/// <summary><see cref="GateRunner"/> when the round passes: no gate is enabled, or every enabled gate exits with code 0 in the card's repository in its sandbox. The report lists each gate, and the round log on the host keeps each gate's command and full output.</summary>
public sealed class GateRunnerPassTests
{
	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithNoGates_PassesWithANoteAndNothingToReport()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync();

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal("No gates are enabled.", verdict.Note);
		Assert.Null(verdict.Report);
		Assert.Empty(await rig.ReadStartedAsync());
		Assert.False(File.Exists(rig.LogPath), "With no gates nothing runs, so nothing is logged.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_RunsEveryGateInOrderInTheCardsRepository()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build"), rig.CreateGate("Test"), rig.CreateGate("Lint"));

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		IReadOnlyList<(string Gate, string Directory)> started = await rig.ReadStartedAsync();
		Assert.Equal(["Build", "Test", "Lint"], started.Select(run => run.Gate));
		Assert.All(started, run => Assert.Equal(rig.RepositoryFolder, Path.GetFullPath(run.Directory), ignoreCase: OperatingSystem.IsWindows()));
		Assert.False(rig.Sandbox.IsSpoiled, "Gates that exited by themselves leave nothing running in the sandbox.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ListsEachPassedGateInOrderInTheSummaryAndCountsThem()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build"), rig.CreateGate("Test"), rig.CreateGate("Lint"));

		Assert.Equal("3 gates passed.", verdict.Note);
		string[] lines = verdict.Report?.Split('\n') ?? [];
		Assert.Equal(3, lines.Length);
		Assert.StartsWith("- Build: passed in ", lines[0], StringComparison.Ordinal);
		Assert.StartsWith("- Test: passed in ", lines[1], StringComparison.Ordinal);
		Assert.StartsWith("- Lint: passed in ", lines[2], StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithOneGate_SaysOneGatePassed()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build"));

		Assert.Equal("1 gate passed.", verdict.Note);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_DoesNotQuoteTheOutputOfGatesThatPassed()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(rig.CreateGate("Build", ["--outcome", "chatty-build-output"]));

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.DoesNotContain("chatty-build-output", verdict.Report, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_KeepsEachGatesCommandAndFullOutputInTheRoundLogInOrder()
	{
		await using GateRunnerRig rig = await GateRunnerRig.CreateAsync();
		Gate first = rig.CreateGate("Build", ["--outcome", "output-of-build"]);
		Gate second = rig.CreateGate("Test", ["--outcome", "output-of-test"]);

		await rig.RunAsync(first, second);

		string log = await File.ReadAllTextAsync(rig.LogPath, TestContext.Current.CancellationToken);
		int buildHeader = log.IndexOf($"== Build: {first.Command} {first.Arguments}\n", StringComparison.Ordinal);
		int buildOutput = log.IndexOf("fake harness: output-of-build", StringComparison.Ordinal);
		int testHeader = log.IndexOf($"== Test: {second.Command} {second.Arguments}\n", StringComparison.Ordinal);
		int testOutput = log.IndexOf("fake harness: output-of-test", StringComparison.Ordinal);
		Assert.True(buildHeader >= 0 && buildHeader < buildOutput && buildOutput < testHeader && testHeader < testOutput, $"The log should hold each gate's full command line and then its output, in order:{Environment.NewLine}{log}");
	}
}
