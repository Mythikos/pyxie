using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> turning what an agent writes in its result file in the sandbox into the verdict of the round.</summary>
public sealed class HarnessRunnerOutcomeTests
{
	private const string Note = "the note";
	private const string Report = "the report\nover two lines";

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Work, "completed", RoundResult.Succeeded)]
	[InlineData(RoundKind.Work, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Work, "blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Review, "approved", RoundResult.Succeeded)]
	[InlineData(RoundKind.Review, "rejected", RoundResult.Failed)]
	[InlineData(RoundKind.Review, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Review, "blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Resolve, "resolved", RoundResult.Succeeded)]
	[InlineData(RoundKind.Resolve, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Resolve, "blocked", RoundResult.Blocked)]
	public async Task RunAsync_MapsEachOutcomeWordToItsResultWithTheNoteAndReport(RoundKind kind, string word, RoundResult expected)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--outcome", word, "--note", Note, "--report", Report);

		RoundVerdict verdict = await rig.RunAsync(agent, kind);

		Assert.Equal(new RoundVerdict(expected, Note, Report), verdict);
		Assert.False(rig.Sandbox.IsSpoiled, "An agent that finished by itself leaves the sandbox usable, whatever it reported.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AnAgentThatReportsNoNoteOrReport_LeavesThemNull()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other));

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, null, null), verdict);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_IgnoresCaseAndSpacesInTheOutcomeWord()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--outcome", "  ApProved ");

		RoundVerdict verdict = await rig.RunAsync(agent, RoundKind.Review);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ReadsAResultFileWithCommentsAndTrailingCommas()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string result = "{\n  // verdict\n  \"outcome\": \"rejected\", /* why */\n  \"note\": \"needs a test\",\n}";
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--raw-result", result);

		RoundVerdict verdict = await rig.RunAsync(agent, RoundKind.Review);

		Assert.Equal(new RoundVerdict(RoundResult.Failed, "needs a test", null), verdict);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ReadsTheResultTheAgentWroteAndLeavesItInTheRepository()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--raw-result", """{"outcome": "blocked", "note": "the task is unclear"}""");

		RoundVerdict verdict = await rig.RunAsync(agent);

		Assert.Equal(new RoundVerdict(RoundResult.Blocked, "the task is unclear", null), verdict);
		Assert.True(File.Exists(rig.ResultFile), "The agent's result file stays in the repository; the next round removes it before its agent starts.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Work, "approved")]
	[InlineData(RoundKind.Work, "rejected")]
	[InlineData(RoundKind.Review, "completed")]
	[InlineData(RoundKind.Work, "finished")]
	[InlineData(RoundKind.Work, "resolved")]
	[InlineData(RoundKind.Resolve, "completed")]
	[InlineData(RoundKind.Resolve, "approved")]
	[InlineData(RoundKind.Resolve, "rejected")]
	public async Task RunAsync_AnOutcomeWordTheRoundDoesNotUse_IsErroredWithTheAgentsOutput(RoundKind kind, string word)
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--outcome", word);

		RoundVerdict verdict = await rig.RunAsync(agent, kind);

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.NotNull(verdict.Note);
		Assert.Contains($"outcome \"{word}\"", verdict.Note, StringComparison.Ordinal);
		Assert.Contains(string.Join(", ", PipelineContract.OutcomeWords(kind)), verdict.Note, StringComparison.Ordinal);
		Assert.Equal($"fake harness: {word}", verdict.Report);
	}
}
