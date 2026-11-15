using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary><c>PipelineController.RunningRounds</c> is how many cards a pipeline worker has a round open for: it rises when a worker claims a card, stays put while the card moves from one round to the next, and returns to zero when the card's run ends, however it ends. The fake workers are held by a file the test creates, so a round stays open exactly as long as the test needs, and the recorder waits on the controller's own announcements instead of sleeping.</summary>
public sealed class PipelineOrchestratorRunningRoundsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardCountsOnceAcrossAllItsRoundsAndTheCountIsZeroBeforeItStartsAndAfterItsRunEnds()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string release) = Marks(harness, "card");
		Agent worker = await harness.AddWorkerAsync("Worker", "--record", started, "--wait-for", release, "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Held card", worker);
		using RunningRoundsRecorder rounds = new(harness.Controller);
		Assert.Equal(0, harness.Controller.RunningRounds);

		await harness.StartOrchestratorAsync();
		Assert.Equal(0, harness.Controller.RunningRounds);

		await harness.StartPipelineAsync();
		await WaitForAgentToStartAsync(harness, started);

		Assert.Equal(1, harness.Controller.RunningRounds);
		await File.WriteAllTextAsync(release, "go", Token);
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await rounds.WhenAsync(counts => counts.Count >= 3 && counts[^1] == 0).WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal("Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal([0, 1, 0], rounds.Counts);
		Assert.Equal(0, harness.Controller.RunningRounds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StoppingThePipeline_ReturnsTheCountToZeroOnceTheRoundIsInterruptedAndResumingCountsItAgain()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string started, string release) = Marks(harness, "card");
		Agent worker = await harness.AddWorkerAsync("Worker", "--record", started, "--wait-for", release, "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Interrupted card", worker);
		using RunningRoundsRecorder rounds = new(harness.Controller);
		await harness.StartPipelineAsync();
		await WaitForAgentToStartAsync(harness, started);
		Assert.Equal(1, harness.Controller.RunningRounds);

		harness.Controller.Stop();
		await rounds.WhenAsync(counts => counts.Count >= 3 && counts[^1] == 0).WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal(0, harness.Controller.RunningRounds);
		Assert.Equal("Work:Interrupted", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));

		await File.WriteAllTextAsync(release, "go", Token);
		harness.Controller.Start();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await rounds.WhenAsync(counts => counts.Count >= 5 && counts[^1] == 0).WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal("Work:Interrupted,Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal([0, 1, 0, 1, 0], rounds.Counts);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WithTwoWorkers_TheCountFollowsHowManyCardsHaveARoundOpen()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(workers: 2, roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		(string firstStarted, string firstRelease) = Marks(harness, "first");
		(string secondStarted, string secondRelease) = Marks(harness, "second");
		Agent firstWorker = await harness.AddWorkerAsync("First worker", "--record", firstStarted, "--wait-for", firstRelease, "--write", "first.txt=1");
		Agent secondWorker = await harness.AddWorkerAsync("Second worker", "--record", secondStarted, "--wait-for", secondRelease, "--write", "second.txt=2");
		WorkCard first = await harness.AddCardAsync("First card", firstWorker);
		WorkCard second = await harness.AddCardAsync("Second card", secondWorker);
		using RunningRoundsRecorder rounds = new(harness.Controller);

		await harness.StartPipelineAsync();
		await WaitForAgentToStartAsync(harness, firstStarted);
		await WaitForAgentToStartAsync(harness, secondStarted);
		await rounds.WhenAsync(counts => counts[^1] == 2).WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal(2, harness.Controller.RunningRounds);

		await File.WriteAllTextAsync(firstRelease, "go", Token);
		await harness.WaitForLaneAsync(first.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await rounds.WhenAsync(counts => counts[^1] == 1).WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal(1, harness.Controller.RunningRounds);

		await File.WriteAllTextAsync(secondRelease, "go", Token);
		await harness.WaitForLaneAsync(second.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await rounds.WhenAsync(counts => counts[^1] == 0).WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal(0, harness.Controller.RunningRounds);
	}

	// The file a held fake agent records its start in, and the file that lets it go on.
	private static (string Started, string Release) Marks(RuntimeHarness harness, string name)
	{
		return (Path.Combine(harness.ScriptsFolder, $"{name}-started.jsonl"), Path.Combine(harness.ScriptsFolder, $"{name}-release"));
	}

	private static async Task WaitForAgentToStartAsync(RuntimeHarness harness, string started)
	{
		await harness.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), "the fake agent to start", RuntimeTestTimeouts.Wait);
	}
}
