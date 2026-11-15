using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A worker that reports it cannot go on without a person stops its card at once, whatever attempts are left, and the pipeline moves on to the cards behind it.</summary>
public sealed class PipelineOrchestratorBlockedTests
{
	private const string Note = "the task is unclear";

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AWorkerThatReportsBlocked_StopsTheCardAtOnceWithItsNote()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--outcome", "blocked", "--note", Note, "--report", "I need to know which file");
		WorkCard card = await harness.AddCardAsync("Unclear task", worker);

		await harness.StartPipelineAsync();
		BoardCard stopped = await harness.WaitForStoppedAsync(card.Id, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Blocked", RoundHistory.Describe(rounds));
		Assert.Equal((Note, "I need to know which file"), (rounds[0].Note, rounds[0].Report));
		Assert.Equal(Note, stopped.History.StopReason);
		Assert.Equal((CardLane.Queue, CardState.Error), (stopped.Card.Lane, stopped.Card.State));
		Assert.True(stopped.Card.AttemptLimit > 1, "The card had attempts left, and a blocked round stops it anyway.");
		Assert.False(stopped.History.IsRunning, "No round is left running on a stopped card.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ABlockedCard_WaitsInTheQueueWhileACardBehindItRunsToTheEnd()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent unclearWorker = await harness.AddWorkerAsync("Unclear worker", "--outcome", "blocked", "--note", Note);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard blocked = await harness.AddCardAsync("Unclear task", unclearWorker);
		WorkCard behind = await harness.AddCardAsync("Clear task", worker);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(behind.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		BoardCard waiting = await harness.WaitForStoppedAsync(blocked.Id, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();
		Assert.Equal("Work:Blocked", RoundHistory.Describe(await harness.GetHistoryAsync(blocked.Id)));
		Assert.Equal(Note, waiting.History.StopReason);
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AWorkerThatCannotBeStarted_StopsTheCardAtOnceBecauseAnotherAttemptWouldFailTheSameWay()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddAgentAsync(new AgentInput("Missing", HarnessKind.Other, "pyxie-no-such-agent-command", string.Empty, CanWork: true, CanReview: false));
		WorkCard card = await harness.AddCardAsync("Doomed task", worker);

		await harness.StartPipelineAsync();
		BoardCard stopped = await harness.WaitForStoppedAsync(card.Id, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Blocked", RoundHistory.Describe(rounds));
		Assert.Contains("Agent 'Missing' could not start", rounds[0].Note, StringComparison.Ordinal);
		Assert.Contains("Agent 'Missing' could not start", stopped.History.StopReason, StringComparison.Ordinal);
	}
}
