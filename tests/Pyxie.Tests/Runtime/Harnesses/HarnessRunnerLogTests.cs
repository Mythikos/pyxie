using Pyxie.Data.Enums;
using Pyxie.Runtime.Harnesses;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> and the round's log file on the host, which keeps the agent's full console output from the sandbox after the verdict has shortened it.</summary>
public sealed class HarnessRunnerLogTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_KeepsTheAgentsConsoleOutputInTheLogFile()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--outcome", "completed"));

		Assert.Equal("fake harness: completed\n", (await File.ReadAllTextAsync(rig.LogPath, Token)).ReplaceLineEndings("\n"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AppendsEachRoundToTheSameLogFile()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--outcome", "errored"));
		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--outcome", "completed"));

		Assert.Equal("fake harness: errored\nfake harness: completed\n", (await File.ReadAllTextAsync(rig.LogPath, Token)).ReplaceLineEndings("\n"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_KeepsTheLogOfAnAgentThatFailed()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--exit", "5"));

		Assert.Contains("fake harness: completed", await File.ReadAllTextAsync(rig.LogPath, Token), StringComparison.Ordinal);
	}
}
