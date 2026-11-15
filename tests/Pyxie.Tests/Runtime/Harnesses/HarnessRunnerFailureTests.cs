using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> when the agent breaks: a failed run, a missing, unreadable, or oversized result file, and a command that cannot start in the sandbox. The first kinds are errors worth another attempt and keep the agent's output; an agent that cannot start needs a person. None of them spoils the sandbox: nothing was stopped part-way.</summary>
public sealed class HarnessRunnerFailureTests
{
	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ANonZeroExit_IsErroredWithTheExitCodeAndTheOutput()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--exit", "3", "--no-result"));

		Assert.Equal(new RoundVerdict(RoundResult.Errored, "Agent 'Worker' exited with code 3.", "fake harness: completed"), verdict);
		Assert.False(rig.Sandbox.IsSpoiled, "An agent that exited by itself leaves nothing running, so the sandbox can be used again.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ANonZeroExit_IsNotRescuedByAValidResultFile()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--exit", "2", "--outcome", "completed"));

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal("Agent 'Worker' exited with code 2.", verdict.Note);
		Assert.True(File.Exists(rig.ResultFile), "The agent did write a valid result; the round still counts as an error because the harness failed.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_KeepsWhatTheAgentWroteOnStandardError()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		// Told to require a different prompt, the fake harness complains on standard error about the one it got, and exits with 64.
		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--require-prompt", "a different prompt"));

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal("Agent 'Worker' exited with code 64.", verdict.Note);
		Assert.Equal($"Expected the prompt on standard input; got: {PipelineContract.Prompt}", verdict.Report);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AnAgentThatWritesNoResultFile_IsErroredWithTheOutput()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--no-result"));

		Assert.Equal(new RoundVerdict(RoundResult.Errored, $"The agent did not write {PipelineContract.ResultFile}.", "fake harness: completed"), verdict);
		Assert.False(rig.Sandbox.IsSpoiled, "An agent that wrote no result still exited by itself.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData("I finished the work.", "is not valid JSON")]
	[InlineData("", "is not valid JSON")]
	[InlineData("{\"outcome\": \"completed\"", "is not valid JSON")]
	[InlineData("[1, 2]", "is not a JSON object")]
	[InlineData("\"completed\"", "is not a JSON object")]
	[InlineData("{\"outcome\": \"maybe\"}", "outcome \"maybe\"")]
	[InlineData("{\"note\": \"no outcome\"}", "outcome \"\"")]
	public async Task RunAsync_AnUnreadableResultFile_IsErroredWithTheReasonAndTheOutput(string result, string expectedProblem)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--raw-result", result));

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.NotNull(verdict.Note);
		Assert.Contains(PipelineContract.ResultFile, verdict.Note, StringComparison.Ordinal);
		Assert.Contains(expectedProblem, verdict.Note, StringComparison.Ordinal);
		Assert.Equal("fake harness: completed", verdict.Report);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AResultFileOverTheLimit_IsErroredAsTooLargeBecauseTheRunnerReadsPastTheLimit()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		// Junk one character over the limit: read whole it is too large; read only up to the limit it would be refused as invalid JSON instead.
		RoundVerdict verdict = await rig.RunAsync(await JunkResultAgentAsync(rig, HarnessResultFile.MaxLength + 1));

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal($"{PipelineContract.ResultFile} is larger than 4 MB.", verdict.Note);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AResultFileOfExactlyTheLimit_IsReadAndJudgedOnWhatItSays()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(await JunkResultAgentAsync(rig, HarnessResultFile.MaxLength));

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.NotNull(verdict.Note);
		Assert.Contains("is not valid JSON", verdict.Note, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ACommandThatIsNotFound_IsBlockedNamingTheAgentAndTheCommand()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string command = $"pyxie-no-such-agent-{Guid.NewGuid():N}";

		RoundVerdict verdict = await rig.RunAsync(new AgentSnapshot("Ghost", HarnessKind.Codex, command, "exec"));

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.Equal($"Agent 'Ghost' could not start: The command {command} was not found.", verdict.Note);
		Assert.Null(verdict.Report);
		Assert.False(rig.Sandbox.IsSpoiled, "Nothing ran, so nothing can be left running in the sandbox.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AFileThatCannotBeStarted_IsBlockedWithTheReason()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string broken = rig.Combine(OperatingSystem.IsWindows() ? "broken.exe" : "broken");
		await File.WriteAllTextAsync(broken, "this is not a program", TestContext.Current.CancellationToken);

		RoundVerdict verdict = await rig.RunAsync(new AgentSnapshot("Broken", HarnessKind.Other, broken, string.Empty));

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.NotNull(verdict.Note);
		Assert.StartsWith($"Agent 'Broken' could not start: The command {broken} could not start: ", verdict.Note, StringComparison.Ordinal);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ABatchFileAgentWithAnArgumentCmdCannotPass_IsBlocked()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string script = await BatchScript.WriteAsync(rig.Folder, "agent.cmd", "@echo ran");

		RoundVerdict verdict = await rig.RunAsync(new AgentSnapshot("Codex", HarnessKind.Codex, script, "exec --note 100%"));

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.NotNull(verdict.Note);
		Assert.StartsWith("Agent 'Codex' could not start: ", verdict.Note, StringComparison.Ordinal);
		Assert.Contains("cannot be passed", verdict.Note, StringComparison.Ordinal);
	}

	// An agent whose result file is the given number of x characters. The text is too long for a command line, so the fake harness reads it from a script, kept outside the sandbox.
	private static async Task<AgentSnapshot> JunkResultAgentAsync(HarnessRig rig, int length)
	{
		string script = await FakeHarnessAgent.WriteScriptAsync(rig.Folder, ["--raw-result", new string('x', length)]);

		return FakeHarnessAgent.ScriptSnapshot("Worker", HarnessKind.Other, script);
	}
}
