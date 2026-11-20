using System.Diagnostics;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Stopping the pipeline while a fake agent or gate is running kills it and records the round as interrupted, without using an attempt; starting again resumes the card at the same stage and attempt before any new card. The interrupted run's first line is a sleep followed by a write of a survivor file, so a process that was not really killed gives itself away by leaving the file.</summary>
public sealed class PipelineOrchestratorStopAndResumeTests
{
	private const string SleepMilliseconds = "4000";
	private const string StoppedNote = "The pipeline was stopped while this round was running.";
	private const string ShutdownNote = "Pyxie shut down while this round was running.";

	// A process that was not killed finishes its four-second sleep within this long of when the test saw it start, and writes its survivor file.
	private static readonly TimeSpan s_survivorDeadline = TimeSpan.FromSeconds(6);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StoppingWhileAWorkerRuns_KillsItAndRecordsTheRoundInterruptedWithoutUsingAnAttempt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string survivor) = Marks(harness);
		string script = await harness.WriteScriptAsync(["--record", started, "--sleep", SleepMilliseconds, "--write", $"{survivor}=alive"], ["--write", "feature.txt=hello"]);
		Agent worker = await harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Worker", HarnessKind.Other, canWork: true, canReview: false, script));
		WorkCard card = await harness.AddCardAsync("Interrupted card", worker);
		await harness.StartPipelineAsync();
		Stopwatch sinceStart = await WaitForAgentToStartAsync(harness, started);

		harness.Controller.Stop();
		BoardCard interrupted = await harness.WaitForCardAsync(card.Id, board => board.History is { WorkRounds: 1, IsRunning: false }, $"card {card.Id}'s round to be interrupted", RuntimeTestTimeouts.Wait);

		CardRound round = Assert.Single(await harness.GetHistoryAsync(card.Id));
		Assert.Equal((RoundKind.Work, RoundOutcome.Interrupted, StoppedNote), (round.Kind, round.Outcome, round.Note));
		Assert.NotNull(round.EndedUtc);
		Assert.Equal((CardLane.InProgress, CardState.Ready, 1), (interrupted.Card.Lane, interrupted.Card.State, interrupted.Card.Attempt));
		Assert.False(harness.Controller.IsRunning, "The pipeline stays stopped.");
		await AssertTheAgentWasKilledAsync(sinceStart, survivor);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingAgain_ResumesTheInterruptedWorkRoundInTheSameAttemptBeforeTheNextCardInTheQueue()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string survivor) = Marks(harness);
		string script = await harness.WriteScriptAsync(["--record", started, "--sleep", SleepMilliseconds, "--write", $"{survivor}=alive"], ["--write", "feature.txt=hello", "--note", "finished after the restart"]);
		Agent worker = await harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Worker", HarnessKind.Other, canWork: true, canReview: false, script));
		Agent otherWorker = await harness.AddWorkerAsync("Other worker", "--write", "other.txt=behind");
		WorkCard card = await harness.AddCardAsync("Interrupted card", worker);
		WorkCard behind = await harness.AddCardAsync("Card behind it", otherWorker);
		await harness.StartPipelineAsync();
		Stopwatch sinceStart = await WaitForAgentToStartAsync(harness, started);
		harness.Controller.Stop();
		await harness.WaitForCardAsync(card.Id, board => board.History is { WorkRounds: 1, IsRunning: false }, $"card {card.Id}'s round to be interrupted", RuntimeTestTimeouts.Wait);
		Assert.Empty(await harness.GetHistoryAsync(behind.Id));

		harness.Controller.Start();
		BoardCard finished = await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.WaitForLaneAsync(behind.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Interrupted,Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 1, 1, 1], rounds.Select(round => round.Attempt));
		Assert.Equal(1, finished.Card.Attempt);
		Assert.Contains("How the previous attempt ended", rounds[1].Briefing, StringComparison.Ordinal);
		Assert.Contains(StoppedNote, rounds[1].Briefing, StringComparison.Ordinal);
		IReadOnlyList<CardRound> behindRounds = await harness.GetHistoryAsync(behind.Id);
		Assert.True(behindRounds[0].StartedUtc >= rounds[^1].EndedUtc, "The interrupted card is resumed, and finishes, before the card behind it is taken.");
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal("behind", await harness.Clone.ReadFileAtAsync("main", "other.txt"));
		await AssertTheAgentWasKilledAsync(sinceStart, survivor);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StoppingWhileAReviewerRuns_ResumesTheReviewWithoutWorkingTheCardAgain()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string survivor) = Marks(harness);
		string script = await harness.WriteScriptAsync(["--record", started, "--sleep", SleepMilliseconds, "--write", $"{survivor}=alive", "--outcome", "approved"], ["--outcome", "approved", "--note", "approved after the restart"]);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent reviewer = await harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Reviewer", HarnessKind.Other, canWork: false, canReview: true, script));
		WorkCard card = await harness.AddCardAsync("Interrupted review", worker, reviewer);
		await harness.StartPipelineAsync();
		Stopwatch sinceStart = await WaitForAgentToStartAsync(harness, started);

		harness.Controller.Stop();
		BoardCard interrupted = await harness.WaitForCardAsync(card.Id, board => board.History is { ReviewRounds: 1, IsRunning: false }, $"card {card.Id}'s review to be interrupted", RuntimeTestTimeouts.Wait);

		Assert.Equal("Work:Completed,Gate:Skipped,Review:Interrupted", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal(CardLane.Review, interrupted.Card.Lane);

		harness.Controller.Start();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Skipped,Review:Interrupted,Review:Approved,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 1, 1, 1], rounds.Select(round => round.Attempt));
		Assert.Equal("approved after the restart", rounds[3].Note);
		await AssertTheAgentWasKilledAsync(sinceStart, survivor);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StoppingWhileAGateRuns_ResumesTheGatesWithoutWorkingTheCardAgain()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string survivor) = Marks(harness);
		string script = await harness.WriteScriptAsync(["--record", started, "--sleep", SleepMilliseconds, "--write", $"{survivor}=alive"], ["--exit", "0"]);
		await harness.AddGateAsync(new GateInput("Check", FakeHarnessAgent.Command, FakeHarnessAgent.ScriptArguments(script), TimeoutMinutes: 5, IsEnabled: true));
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Interrupted gate", worker);
		await harness.StartPipelineAsync();
		Stopwatch sinceStart = await WaitForAgentToStartAsync(harness, started);

		harness.Controller.Stop();
		BoardCard interrupted = await harness.WaitForCardAsync(card.Id, board => board.History is { IsRunning: false } && board.History.GateRounds == 1, $"card {card.Id}'s gate round to be interrupted", RuntimeTestTimeouts.Wait);

		Assert.Equal("Work:Completed,Gate:Interrupted", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal(CardLane.Gates, interrupted.Card.Lane);

		harness.Controller.Start();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Interrupted,Gate:Passed,Review:Skipped,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 1, 1, 1], rounds.Select(round => round.Attempt));
		await AssertTheAgentWasKilledAsync(sinceStart, survivor);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ShuttingPyxieDownWhileAWorkerRuns_KillsItAndSaysThatPyxieShutDown()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string survivor) = Marks(harness);
		Agent worker = await harness.AddWorkerAsync("Worker", "--record", started, "--sleep", SleepMilliseconds, "--write", $"{survivor}=alive");
		WorkCard card = await harness.AddCardAsync("Interrupted card", worker);
		await harness.StartPipelineAsync();
		Stopwatch sinceStart = await WaitForAgentToStartAsync(harness, started);

		await harness.StopOrchestratorAsync();

		CardRound round = Assert.Single(await harness.GetHistoryAsync(card.Id));
		Assert.Equal((RoundOutcome.Interrupted, ShutdownNote), (round.Outcome, round.Note));
		Assert.Equal(CardLane.InProgress, (await harness.GetBoardAsync()).Single().Card.Lane);
		await AssertTheAgentWasKilledAsync(sinceStart, survivor);
	}

	// Files the fake agents write, outside the repository and every sandbox: one when the run starts, and one a run that was not killed would write after its sleep.
	private static (string Started, string Survivor) Marks(RuntimeHarness harness)
	{
		return (Path.Combine(harness.ScriptsFolder, "started.jsonl"), Path.Combine(harness.ScriptsFolder, "survivor.txt"));
	}

	private static async Task<Stopwatch> WaitForAgentToStartAsync(RuntimeHarness harness, string started)
	{
		await harness.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), "the fake agent to start", RuntimeTestTimeouts.Wait);

		return Stopwatch.StartNew();
	}

	private static async Task AssertTheAgentWasKilledAsync(Stopwatch sinceStart, string survivor)
	{
		TimeSpan remaining = s_survivorDeadline - sinceStart.Elapsed;
		if (remaining > TimeSpan.Zero)
		{
			await Task.Delay(remaining, Token);
		}

		Assert.False(File.Exists(survivor), "The interrupted agent finished its sleep and wrote its file: it was not killed when the pipeline stopped.");
	}
}
