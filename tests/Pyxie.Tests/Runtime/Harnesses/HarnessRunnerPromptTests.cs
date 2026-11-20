using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> and how the agent is started in the sandbox: the fixed prompt goes on standard input; the configured arguments are split the way Windows splits them; the agent runs in the card's repository; and the command carries the round's timeout and log.</summary>
public sealed class HarnessRunnerPromptTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(HarnessKind.ClaudeCode)]
	[InlineData(HarnessKind.Codex)]
	[InlineData(HarnessKind.DeepSeek)]
	[InlineData(HarnessKind.Other)]
	public async Task RunAsync_SendsThePromptOnStandardInputAndNotAsAnArgument(HarnessKind harness)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Agent", harness, "--record", record, "--require-prompt", PipelineContract.Prompt));

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		FakeHarnessRecord run = await FakeHarnessRecord.ReadSingleAsync(record);
		Assert.Equal(PipelineContract.Prompt, run.Input);
		Assert.Equal(["--record", record, "--require-prompt", PipelineContract.Prompt], run.Arguments);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_RunsTheAgentInTheCardsRepository()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record));

		Assert.Equal(rig.RepositoryFolder, (await FakeHarnessRecord.ReadSingleAsync(record)).Directory, ignoreCase: OperatingSystem.IsWindows());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_HandsTheSandboxTheRoundsTimeoutAndLogWithTheCommand()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		RecordingSandbox recorder = new(rig.Sandbox);
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.ClaudeCode);
		TimeSpan timeout = TimeSpan.FromMinutes(37);

		await rig.StartAsync(HarnessRig.Work(agent), timeout, Token, recorder).WithinLimitAsync();

		SandboxCommand command = Assert.Single(recorder.Commands);
		Assert.Equal(agent.Command, command.Command);
		Assert.Equal(rig.RepositoryPath, command.WorkingDirectory);
		Assert.Equal(timeout, command.Timeout);
		Assert.Equal(rig.LogPath, command.LogPath);
		Assert.Equal(PipelineContract.Prompt, command.StandardInput);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_SplitsTheConfiguredArgumentsTheWayWindowsDoes()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		string arguments = $"\"{FakeHarnessAgent.DllPath}\"  --record \"{record}\"\t--note \"two  words\" --report \"say \\\"hi\\\"\"";

		RoundVerdict verdict = await rig.RunAsync(new AgentSnapshot("Agent", HarnessKind.Other, FakeHarnessAgent.Command, arguments, AgentAuthentication.None));

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "two  words", "say \"hi\""), verdict);
		Assert.Equal(["--record", record, "--note", "two  words", "--report", "say \"hi\""], (await FakeHarnessRecord.ReadSingleAsync(record)).Arguments);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithoutConfiguredArguments_StartsTheCommandOnItsOwn()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		string launcher = await BatchScript.WriteAsync(rig.Folder, "agent.cmd", $"@\"{FakeHarnessAgent.Command}\" \"{FakeHarnessAgent.DllPath}\" --record \"{record}\"");

		RoundVerdict verdict = await rig.RunAsync(new AgentSnapshot("Agent", HarnessKind.ClaudeCode, launcher, string.Empty, AgentAuthentication.Account));

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(PipelineContract.Prompt, (await FakeHarnessRecord.ReadSingleAsync(record)).Input);
	}
}
