using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A database failure never stops the host. The failures here are database failures (a locked file), which the orchestrator used to tolerate only when they were I/O or git failures: one at start-up is logged and retried before the pipeline claims anything, and one while cleaning up abandoned workspaces is logged and the pipeline goes on. <see cref="DatabaseFailureRig"/> makes the context factory fail on demand.</summary>
public sealed class PipelineOrchestratorDatabaseFailureTests
{
	private const string RecoveryFailed = "Could not mark the rounds left running by the previous run of Pyxie as interrupted";
	private const string CleanupFailed = "Could not remove the workspaces of cards that no longer need them";
	private const string ClaimFailed = "Claiming the next card failed";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<(WorkCard Card, WorkCard Waiting)> LeaveARoundOpenAsync(DatabaseFailureRig rig, TempGitRepository source)
	{
		await rig.ConnectRepositoryAsync(source);
		Agent worker = await rig.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await rig.AddCardAsync("Left running", worker);
		WorkCard waiting = await rig.AddCardAsync("Waiting", worker);
		PipelineWork left = await rig.ClaimAsync();
		Assert.Equal(card.Id, left.CardId);

		return (card, waiting);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingTheOrchestrator_WhenTheDatabaseFails_LogsItAndKeepsRunning()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DatabaseFailureRig rig = new();
		(WorkCard card, _) = await LeaveARoundOpenAsync(rig, source);
		rig.Factory.Failing = true;

		await rig.StartOrchestratorAsync();
		await Task.Delay(RuntimeTestTimeouts.IdleObservation, Token);

		Assert.False(rig.Orchestrator.ExecuteTask?.IsCompleted, "A database failure at start-up must not end the pipeline, which would stop the host.");
		CapturedLogEntry failure = Assert.Single(rig.Logs.Entries, entry => entry.Message.Contains(RecoveryFailed, StringComparison.Ordinal));
		Assert.Equal(LogLevel.Error, failure.Level);
		Assert.IsType<SqliteException>(failure.Exception);
		CardRound open = Assert.Single(await rig.ReadRoundsAsync(card.Id));
		Assert.Null(open.Outcome);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AStartedPipeline_WhileTheDatabaseKeepsFailing_RetriesTheRecoveryAndClaimsNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DatabaseFailureRig rig = new();
		(WorkCard card, WorkCard waiting) = await LeaveARoundOpenAsync(rig, source);
		rig.Factory.Failing = true;
		await rig.StartOrchestratorAsync();

		rig.Controller.Start();
		await rig.WaitUntilAsync(() => Task.FromResult(rig.Logs.Count(LogLevel.Error, RecoveryFailed) >= 3), "the recovery to be tried again at every poll");

		Assert.False(rig.Orchestrator.ExecuteTask?.IsCompleted, "The pipeline keeps trying instead of ending.");
		Assert.Equal(0, rig.Logs.Count(LogLevel.Error, ClaimFailed));
		Assert.Equal(CardLane.InProgress, (await rig.ReadLaneAsync(card.Id)).Lane);
		Assert.Equal((CardLane.Queue, CardState.Ready), await rig.ReadLaneAsync(waiting.Id));
		Assert.Empty(await rig.ReadRoundsAsync(waiting.Id));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AStartedPipeline_OnceTheDatabaseWorksAgain_RecoversTheOpenRoundBeforeClaimingAndResumesTheCard()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DatabaseFailureRig rig = new();
		(WorkCard card, WorkCard waiting) = await LeaveARoundOpenAsync(rig, source);
		rig.Factory.Failing = true;
		await rig.StartOrchestratorAsync();
		rig.Controller.Start();
		await rig.WaitUntilAsync(() => Task.FromResult(rig.Logs.Count(LogLevel.Error, RecoveryFailed) >= 2), "the recovery to fail and be tried again");

		rig.Factory.Failing = false;
		await rig.WaitUntilAsync(async () => (await rig.ReadLaneAsync(card.Id)).Lane == CardLane.Complete && (await rig.ReadLaneAsync(waiting.Id)).Lane == CardLane.Complete, "both cards to be merged once the database works again");

		IReadOnlyList<CardRound> rounds = await rig.ReadRoundsAsync(card.Id);
		Assert.Equal((RoundKind.Work, RoundOutcome.Interrupted), (rounds[0].Kind, rounds[0].Outcome));
		Assert.Contains("Pyxie stopped", rounds[0].Note, StringComparison.Ordinal);
		Assert.Equal(["Work:Interrupted", "Work:Completed", "Gate:Skipped", "Review:Skipped", "Merge:Merged"], rounds.Select(round => $"{round.Kind}:{round.Outcome}"));
		Assert.False(rig.Orchestrator.ExecuteTask?.IsCompleted, "The pipeline survived the outage.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingThePipeline_WhenTheDatabaseFailsWhileCleaningUpAbandonedWorkspaces_LogsItAndGoesOnWithTheCards()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DatabaseFailureRig rig = new();
		(WorkCard card, WorkCard waiting) = await LeaveARoundOpenAsync(rig, source);
		await rig.StartOrchestratorAsync();
		await rig.WaitUntilAsync(async () => (await rig.ReadRoundsAsync(card.Id)).Single().Outcome == RoundOutcome.Interrupted, "the start-up recovery to finish");

		rig.Factory.FailNext(1);
		rig.Controller.Start();
		await rig.WaitUntilAsync(async () => (await rig.ReadLaneAsync(card.Id)).Lane == CardLane.Complete && (await rig.ReadLaneAsync(waiting.Id)).Lane == CardLane.Complete, "both cards to be merged after the failed cleanup");

		Assert.Equal(1, rig.Factory.Failures);
		CapturedLogEntry warning = Assert.Single(rig.Logs.Entries, entry => entry.Message.Contains(CleanupFailed, StringComparison.Ordinal));
		Assert.Equal(LogLevel.Warning, warning.Level);
		Assert.IsType<SqliteException>(warning.Exception);
		Assert.Equal(0, rig.Logs.Count(LogLevel.Error, ClaimFailed));
		Assert.False(rig.Orchestrator.ExecuteTask?.IsCompleted, "A failed cleanup must not end the pipeline.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task APipelineThatCannotClaim_LogsEachFailureAndTriesAgainAtTheNextPoll()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DatabaseFailureRig rig = new();
		(WorkCard card, WorkCard waiting) = await LeaveARoundOpenAsync(rig, source);
		await rig.StartOrchestratorAsync();
		await rig.WaitUntilAsync(async () => (await rig.ReadRoundsAsync(card.Id)).Single().Outcome == RoundOutcome.Interrupted, "the start-up recovery to finish");

		rig.Factory.FailNext(3);
		rig.Controller.Start();
		await rig.WaitUntilAsync(async () => (await rig.ReadLaneAsync(card.Id)).Lane == CardLane.Complete && (await rig.ReadLaneAsync(waiting.Id)).Lane == CardLane.Complete, "both cards to be merged after the failed claims");

		Assert.Equal(3, rig.Factory.Failures);
		Assert.Equal(1, rig.Logs.Count(LogLevel.Warning, CleanupFailed));
		Assert.Equal(2, rig.Logs.Count(LogLevel.Error, ClaimFailed));
		Assert.False(rig.Orchestrator.ExecuteTask?.IsCompleted, "Failed claims must not end the pipeline.");
	}
}
