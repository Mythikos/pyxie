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

/// <summary><see cref="HarnessRunner"/> and the tokens in an agent's arguments: the harness is started with each token filled in with the round's values, a value stays one argument and is not filled in again, a shell's <c>${NAME}</c> is passed on as it is, and a name in capitals that is not a token blocks the round before anything reaches the sandbox.</summary>
public sealed class HarnessRunnerTokenTests
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(2);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	// The round the tokens' values come from: card 12, "Fix the login page", the third attempt of a review (the harness is told to approve, the one word a review accepts besides rejecting).
	private static PipelineWork Round(AgentSnapshot agent, RoundKind kind = RoundKind.Review, string cardName = "Fix the login page")
	{
		return new PipelineWork(CardId: 12, cardName, RoundId: 1, Sequence: 1, kind, Attempt: 3, agent, HarnessRig.DefaultBriefing);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_StartsTheHarnessWithEveryTokenFilledInWithTheRoundsValues()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.ClaudeCode, "--record", record, "--outcome", "approved", "--note", "{CARD_NAME}", "--report", "{BRANCH}/{STAGE}/{ATTEMPT}/{CARD_ID}");

		RoundVerdict verdict = await rig.StartAsync(Round(agent), s_timeout, Token).WithinLimitAsync();

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "Fix the login page", "pyxie/card-12/Review/3/12"), verdict);
		FakeHarnessRecord run = await FakeHarnessRecord.ReadSingleAsync(record);
		Assert.Equal(["--record", record, "--outcome", "approved", "--note", "Fix the login page", "--report", "pyxie/card-12/Review/3/12"], run.Arguments);
		Assert.Equal(PipelineContract.Prompt, run.Input);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_FillsInATokenInsideAQuotedArgumentWithSpaces()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Codex, "--record", record, "--note", "{CARD_NAME} (attempt {ATTEMPT} of {STAGE})");

		RoundVerdict verdict = await rig.StartAsync(Round(agent, RoundKind.Work), s_timeout, Token).WithinLimitAsync();

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "Fix the login page (attempt 3 of Work)", null), verdict);
		Assert.Equal(["--record", record, "--note", "Fix the login page (attempt 3 of Work)"], (await FakeHarnessRecord.ReadSingleAsync(record)).Arguments);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData("Say \"hi\" to  everyone")]
	[InlineData("{BRANCH} and {CARD_ID}")]
	[InlineData("{NOPE}")]
	public async Task RunAsync_PassesACardNameAsExactlyOneArgumentAndDoesNotFillItInAgain(string cardName)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.ClaudeCode, "--record", record, "--outcome", "approved", "--note", "{CARD_NAME}");

		RoundVerdict verdict = await rig.StartAsync(Round(agent, cardName: cardName), s_timeout, Token).WithinLimitAsync();

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, cardName, null), verdict);
		Assert.Equal(["--record", record, "--outcome", "approved", "--note", cardName], (await FakeHarnessRecord.ReadSingleAsync(record)).Arguments);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForDeepSeek_FillsInTheArgumentsAndSendsThePromptUnchanged()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");

		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.DeepSeek, "--record", record, "--note", "{CARD_NAME} {BRANCH}");

		RoundVerdict verdict = await rig.StartAsync(Round(agent, RoundKind.Work), s_timeout, Token).WithinLimitAsync();

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "Fix the login page pyxie/card-12", null), verdict);
		FakeHarnessRecord run = await FakeHarnessRecord.ReadSingleAsync(record);
		Assert.Equal(PipelineContract.Prompt, run.Input);
		Assert.Equal(["--record", record, "--note", "Fix the login page pyxie/card-12"], run.Arguments);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_PassesAShellsVariableOnAsItIsBesideATokenBecauseNoShellIsInvolved()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record, "--note", "${HOME}/{CARD_ID}", "--report", "${CARD_NAME}");

		RoundVerdict verdict = await rig.StartAsync(Round(agent, RoundKind.Work), s_timeout, Token).WithinLimitAsync();

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "${HOME}/12", "${CARD_NAME}"), verdict);
		Assert.Equal(["--record", record, "--note", "${HOME}/12", "--report", "${CARD_NAME}"], (await FakeHarnessRecord.ReadSingleAsync(record)).Arguments);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithAnUnknownToken_BlocksTheRoundBeforeAnythingReachesTheSandbox()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string record = rig.Combine("record.txt");
		RecordingSandbox recorder = new(rig.Sandbox);
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.ClaudeCode, "--record", record, "--note", "{NOPE}");

		RoundVerdict verdict = await rig.StartAsync(Round(agent), s_timeout, Token, recorder).WithinLimitAsync();

		Assert.Equal(RoundVerdict.Blocked("Agent 'Agent' cannot start. Arguments use {NOPE}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}."), verdict);
		Assert.Empty(recorder.Calls);
		Assert.Empty(recorder.Commands);
		Assert.False(File.Exists(rig.BriefingFile), "No briefing should have been written for a round that cannot start.");
		Assert.False(Directory.Exists(rig.RepositoryFolder), "Nothing should have been written to the card's repository.");
		Assert.False(File.Exists(record), "The harness should not have been started.");
	}
}
