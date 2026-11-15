using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>The pipeline spends nothing while it is stopped, and cannot be started while no project is open. Each test watches the idle pipeline for a while with a fake agent that would leave a mark if it ran, and then makes the pipeline able to work and sees the very same card run, so the idleness was the pipeline's choice and not a card that could never run.</summary>
public sealed class PipelineOrchestratorIdleTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ThePipelineAfterLaunch_DoesNothingUntilItIsStarted()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		string ran = Path.Combine(harness.ScriptsFolder, "ran.jsonl");
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--record", ran, "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Waiting card", worker);
		await harness.StartOrchestratorAsync();
		harness.NotifyCardsChanged();

		await Task.Delay(RuntimeTestTimeouts.IdleObservation, Token);

		BoardCard idle = Assert.Single(await harness.GetBoardAsync());
		Assert.Equal((CardLane.Queue, CardState.Ready, 0), (idle.Card.Lane, idle.Card.State, idle.Card.Attempt));
		Assert.Equal(CardHistorySummary.Empty, idle.History);
		Assert.False(File.Exists(ran), "No agent may start while the pipeline is stopped.");

		harness.Controller.Start();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AStoppedPipeline_DoesNotTakeCardsThatAreAddedAfterItStopped()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		string ran = Path.Combine(harness.ScriptsFolder, "ran.jsonl");
		await harness.ConnectRepositoryAsync(source);
		Agent first = await harness.AddWorkerAsync("First worker", "--write", "first.txt=1");
		Agent second = await harness.AddWorkerAsync("Second worker", "--record", ran, "--write", "second.txt=2");
		WorkCard ranBeforeStopping = await harness.AddCardAsync("Runs before the stop", first);
		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(ranBeforeStopping.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		harness.Controller.Stop();
		WorkCard addedAfterStopping = await harness.AddCardAsync("Added after the stop", second);

		await Task.Delay(RuntimeTestTimeouts.IdleObservation, Token);

		BoardCard waiting = (await harness.GetBoardAsync()).Single(card => card.Card.Id == addedAfterStopping.Id);
		Assert.Equal((CardLane.Queue, CardState.Ready), (waiting.Card.Lane, waiting.Card.State));
		Assert.Equal(CardHistorySummary.Empty, waiting.History);
		Assert.False(File.Exists(ran), "No agent may start after the pipeline was stopped.");

		harness.Controller.Start();
		await harness.WaitForLaneAsync(addedAfterStopping.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();
		Assert.Equal("2", await harness.Clone.ReadFileAtAsync("main", "second.txt"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ThePipelineWithNoProjectOpen_CannotBeStartedAndRunsTheCardOnceAProjectIsImported()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.StartOrchestratorAsync();

		string refused = ResultAssert.Failed(harness.Controller.Start());

		Assert.Equal("No project is open.", refused);
		Assert.False(harness.Controller.IsRunning, "The pipeline stays stopped while no project is open.");

		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Waiting card", worker);
		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}
}
