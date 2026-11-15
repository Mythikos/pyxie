using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Runtime.Projects;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Rounds a previous run of Pyxie left open in the open project are marked interrupted when the orchestrator starts, or when the project is opened later, before the pipeline is started, and the cards resume at the same stage. The tests open rounds the way a crashed Pyxie would have left them: by claiming cards through the pipeline service and never reporting a result.</summary>
public sealed class PipelineOrchestratorRecoveryTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingThePipelineApp_MarksEveryRoundLeftOpenInterruptedAndLeavesTheCardsInTheirLanes()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard first = await harness.AddCardAsync("First", worker);
		WorkCard second = await harness.AddCardAsync("Second", worker);
		WorkCard waiting = await harness.AddCardAsync("Still waiting", worker);
		PipelineWork openFirst = await ClaimAsync(harness);
		PipelineWork openSecond = await ClaimAsync(harness);
		Assert.Equal([first.Id, second.Id], [openFirst.CardId, openSecond.CardId]);
		Assert.True((await harness.GetBoardAsync()).Count(card => card.History.IsRunning) == 2, "Both rounds are open before the orchestrator starts.");

		await harness.StartOrchestratorAsync();
		await harness.WaitUntilAsync(async () => !(await harness.GetBoardAsync()).Any(card => card.History.IsRunning), "the open rounds to be marked interrupted", RuntimeTestTimeouts.Wait);

		foreach (WorkCard card in new[] { first, second })
		{
			CardRound round = Assert.Single(await harness.GetHistoryAsync(card.Id));
			Assert.Equal((RoundKind.Work, RoundOutcome.Interrupted), (round.Kind, round.Outcome));
			Assert.Contains("Pyxie stopped", round.Note, StringComparison.Ordinal);
			Assert.NotNull(round.EndedUtc);
		}

		IReadOnlyList<BoardCard> board = await harness.GetBoardAsync();
		Assert.Equal([CardLane.InProgress, CardLane.InProgress, CardLane.Queue], board.OrderBy(card => card.Card.Id).Select(card => card.Card.Lane));
		Assert.Equal(CardHistorySummary.Empty, board.Single(card => card.Card.Id == waiting.Id).History);
		Assert.False(harness.Controller.IsRunning, "Recovering rounds does not start the pipeline.");
		Assert.Equal(0, harness.Controller.RunningRounds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingThePipelineApp_AnnouncesThatCardsChangedSoOpenBoardsRefresh()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker");
		await harness.AddCardAsync("Left running", worker);
		await ClaimAsync(harness);
		TaskCompletionSource announced = new(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnChanged()
		{
			announced.TrySetResult();
		}

		harness.Activity.CardsChanged += OnChanged;

		try
		{
			await harness.StartOrchestratorAsync();
			await announced.Task.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		}
		finally
		{
			harness.Activity.CardsChanged -= OnChanged;
		}

		Assert.True(announced.Task.IsCompletedSuccessfully, "Recovering a round announces a change.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AfterRecovery_StartingThePipelineResumesTheInterruptedCardsAtTheSameStageAndAttemptAndFinishesThem()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Left running", worker);
		await ClaimAsync(harness);
		await harness.StartOrchestratorAsync();
		await harness.WaitUntilAsync(async () => !(await harness.GetBoardAsync()).Any(board => board.History.IsRunning), "the open round to be marked interrupted", RuntimeTestTimeouts.Wait);

		harness.Controller.Start();
		BoardCard finished = await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Interrupted,Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 1, 1, 1], rounds.Select(round => round.Attempt));
		Assert.Equal(1, finished.Card.Attempt);
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task OpeningAProjectWhileThePipelineIsStopped_MarksTheRoundsItsLastRunLeftOpenInterrupted()
	{
		await using TempGitRepository first = await TempGitRepository.CreateAsync();
		await using TempGitRepository second = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(first);
		int firstProject = harness.OpenProject.Current?.Id ?? throw new InvalidOperationException("The import did not open its project.");
		Agent worker = await harness.AddWorkerAsync("Worker");
		WorkCard card = await harness.AddCardAsync("Left running", worker);
		await ClaimAsync(harness);
		await using (AsyncServiceScope scope = harness.CreateScope())
		{
			ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ProjectSessions>().CloseAsync(firstProject, Token));
		}

		await harness.ConnectRepositoryAsync(second);
		int secondProject = harness.OpenProject.Current?.Id ?? throw new InvalidOperationException("The import did not open its project.");
		await harness.StartOrchestratorAsync();
		await using (AsyncServiceScope scope = harness.CreateScope())
		{
			ProjectSessions sessions = scope.ServiceProvider.GetRequiredService<ProjectSessions>();
			ResultAssert.Succeeded(await sessions.CloseAsync(secondProject, Token));
			ResultAssert.Succeeded(await sessions.OpenAsync(firstProject, Token));
		}

		await harness.WaitUntilAsync(async () => !(await harness.GetBoardAsync()).Any(board => board.History.IsRunning), "the reopened project's open round to be marked interrupted", RuntimeTestTimeouts.Wait);

		CardRound round = Assert.Single(await harness.GetHistoryAsync(card.Id));
		Assert.Equal((RoundKind.Work, RoundOutcome.Interrupted), (round.Kind, round.Outcome));
		Assert.False(harness.Controller.IsRunning, "Recovering rounds does not start the pipeline.");
	}

	// Claims the next card the way a pipeline worker does, which opens its round and leaves it open.
	private static async Task<PipelineWork> ClaimAsync(RuntimeHarness harness)
	{
		await using AsyncServiceScope scope = harness.CreateScope();
		PipelineWork? work = await scope.ServiceProvider.GetRequiredService<ICardPipelineService>().ClaimNextAsync(Token);

		return work ?? throw new InvalidOperationException("The pipeline had no card to claim.");
	}
}
