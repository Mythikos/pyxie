using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> when a round cannot run an agent at all: it is blocked at once, before anything is asked of the sandbox.</summary>
public sealed class HarnessRunnerGuardTests
{
	private const string NoAgentNote = "No agent was recorded for this round.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithoutARecordedAgent_IsBlockedWithoutUsingTheSandbox()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		RecordingSandbox recorder = new(rig.Sandbox);

		RoundVerdict verdict = await rig.StartAsync(HarnessRig.Work(agent: null), TimeSpan.FromMinutes(1), Token, recorder).WithinLimitAsync();

		Assert.Equal(RoundVerdict.Blocked(NoAgentNote), verdict);
		Assert.Empty(recorder.Calls);
		Assert.False(Directory.Exists(rig.RepositoryFolder), "A blocked round must not touch the repository.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Merge)]
	public async Task RunAsync_ARoundThatRunsNoAgent_IsBlockedAndNeverStartsTheAgent(RoundKind kind)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		RecordingSandbox recorder = new(rig.Sandbox);
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record);

		RoundVerdict verdict = await rig.StartAsync(HarnessRig.Work(agent, kind), TimeSpan.FromMinutes(1), Token, recorder).WithinLimitAsync();

		Assert.Equal(RoundVerdict.Blocked(NoAgentNote), verdict);
		Assert.Empty(recorder.Calls);
		Assert.False(File.Exists(record), $"A {kind} round does not run an agent, but the agent was started.");
		Assert.False(Directory.Exists(rig.RepositoryFolder), "A blocked round must not touch the repository.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public async Task RunAsync_AnAgentWithoutACommand_IsBlockedAndNamesTheAgent(string command)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		RecordingSandbox recorder = new(rig.Sandbox);
		AgentSnapshot agent = new("Writer", HarnessKind.ClaudeCode, command, string.Empty, AgentAuthentication.Account);

		RoundVerdict verdict = await rig.StartAsync(HarnessRig.Work(agent), TimeSpan.FromMinutes(1), Token, recorder).WithinLimitAsync();

		Assert.Equal(RoundVerdict.Blocked("Agent 'Writer' has no command to run."), verdict);
		Assert.Empty(recorder.Calls);
		Assert.False(Directory.Exists(rig.RepositoryFolder), "A blocked round must not touch the repository.");
	}
}
