using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Every round runs in a sandbox, so a pipeline that cannot make one stops at once and says why (<c>PipelineController.StopReason</c>) instead of failing each card it picks up. A container engine that stops answering during a round is not the card's fault: the round is given back without using an attempt, and it resumes when the pipeline starts again.</summary>
public sealed class PipelineOrchestratorSandboxTests
{
	private const string Unavailable = "Docker is not running. Start Docker Desktop, then start the pipeline.";
	private const string StoppedAnswering = "Docker stopped answering.";

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingThePipelineWhileSandboxesCannotBeMade_StopsItWithTheReasonAndClaimsNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using SandboxRig rig = new();
		await rig.ConnectRepositoryAsync(source);
		Agent worker = await rig.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await rig.AddCardAsync("Waiting card", worker);
		rig.Sandboxes.UnavailableReason = Unavailable;

		await rig.StartPipelineAsync();
		await WaitForThePipelineToStopAsync(rig);

		Assert.Equal(Unavailable, rig.Controller.StopReason);
		BoardCard waiting = Assert.Single(await rig.GetBoardAsync());
		Assert.Equal((card.Id, CardLane.Queue, CardState.Ready, 0), (waiting.Card.Id, waiting.Card.Lane, waiting.Card.State, waiting.Card.Attempt));
		Assert.Empty(await rig.GetHistoryAsync(card.Id));
		Assert.Empty(rig.Sandboxes.Local.Started);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingAgainOnceSandboxesCanBeMade_ClearsTheReasonAndRunsTheCard()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using SandboxRig rig = new();
		await rig.ConnectRepositoryAsync(source);
		Agent worker = await rig.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await rig.AddCardAsync("Waiting card", worker);
		rig.Sandboxes.UnavailableReason = Unavailable;
		await rig.StartPipelineAsync();
		await WaitForThePipelineToStopAsync(rig);

		rig.Sandboxes.UnavailableReason = null;
		ResultAssert.Succeeded(rig.Controller.Start());

		Assert.Null(rig.Controller.StopReason);
		await rig.WaitForLaneAsync(card.Id, CardLane.Complete);
		Assert.Equal("Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(await rig.GetHistoryAsync(card.Id)));
		Assert.Equal("hello", await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
		Assert.True(rig.Controller.IsRunning, "The pipeline keeps running once sandboxes can be made.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AContainerEngineThatStopsAnsweringDuringARound_InterruptsTheRoundWithoutUsingAnAttemptAndStopsThePipelineWithTheReason()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using SandboxRig rig = new();
		await rig.ConnectRepositoryAsync(source);
		Agent worker = await rig.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await rig.AddCardAsync("Interrupted card", worker);
		rig.Sandboxes.StartFailure = new SandboxUnavailableException(StoppedAnswering);
		using RunningRoundsRecorder rounds = new(rig.Controller);

		await rig.StartPipelineAsync();
		BoardCard interrupted = await rig.WaitForCardAsync(card.Id, board => board.History is { WorkRounds: 1, IsRunning: false }, "the work round to be given back");
		await WaitForThePipelineToStopAsync(rig);
		await rounds.WhenAsync(counts => counts.Count >= 3 && counts[^1] == 0).WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

		Assert.Equal([0, 1, 0], rounds.Counts);
		Assert.Equal(StoppedAnswering, rig.Controller.StopReason);
		CardRound round = Assert.Single(await rig.GetHistoryAsync(card.Id));
		Assert.Equal((RoundKind.Work, RoundOutcome.Interrupted), (round.Kind, round.Outcome));
		Assert.Equal($"{StoppedAnswering} The round resumes when the pipeline starts again.", round.Note);
		Assert.Equal((CardLane.InProgress, CardState.Ready, 1), (interrupted.Card.Lane, interrupted.Card.State, interrupted.Card.Attempt));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingAgainOnceTheContainerEngineAnswers_ResumesTheGivenBackRoundInTheSameAttemptAndClearsTheReason()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using SandboxRig rig = new();
		await rig.ConnectRepositoryAsync(source);
		Agent worker = await rig.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await rig.AddCardAsync("Interrupted card", worker);
		rig.Sandboxes.StartFailure = new SandboxUnavailableException(StoppedAnswering);
		await rig.StartPipelineAsync();
		await rig.WaitForCardAsync(card.Id, board => board.History is { WorkRounds: 1, IsRunning: false }, "the work round to be given back");
		await WaitForThePipelineToStopAsync(rig);

		rig.Sandboxes.StartFailure = null;
		ResultAssert.Succeeded(rig.Controller.Start());

		Assert.Null(rig.Controller.StopReason);
		BoardCard finished = await rig.WaitForLaneAsync(card.Id, CardLane.Complete);
		IReadOnlyList<CardRound> rounds = await rig.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Interrupted,Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 1, 1, 1], rounds.Select(round => round.Attempt));
		Assert.Equal(1, finished.Card.Attempt);
		Assert.Equal("hello", await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
	}

	private static async Task WaitForThePipelineToStopAsync(SandboxRig rig)
	{
		await rig.WaitUntilAsync(() => Task.FromResult(!rig.Controller.IsRunning), "the pipeline to stop by itself");
	}
}
