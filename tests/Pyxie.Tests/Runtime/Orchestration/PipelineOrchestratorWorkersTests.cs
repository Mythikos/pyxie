using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary><c>Pipeline:Workers</c> is how many cards run at once. The fake workers sleep, so a card's rounds last long enough to tell running one at a time from running together, and the recorder counts the rounds that were running at every announcement.</summary>
public sealed class PipelineOrchestratorWorkersTests
{
	private const string SleepMilliseconds = "2000";
	private const string SlowSleepMilliseconds = "9000";

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WithOneWorker_CardsRunOneAfterTheOtherInQueueOrder()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(workers: 1, roundTimeoutMinutes: 1);
		using BoardChangeRecorder board = new(harness);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--sleep", SleepMilliseconds);
		WorkCard first = await harness.AddCardAsync("First", worker);
		WorkCard second = await harness.AddCardAsync("Second", worker);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(first.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.WaitForLaneAsync(second.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		board.ThrowIfFailed();
		Assert.Equal(1, board.MaxOpenRounds);
		IReadOnlyList<CardRound> firstRounds = await harness.GetHistoryAsync(first.Id);
		IReadOnlyList<CardRound> secondRounds = await harness.GetHistoryAsync(second.Id);
		Assert.True(secondRounds[0].StartedUtc >= firstRounds[^1].EndedUtc, "The second card must wait until the first card's last round ended.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WithTwoWorkers_TwoCardsRunAtTheSameTimeAndTheThirdWaitsForAFreeWorker()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(workers: 2, roundTimeoutMinutes: 1);
		using BoardChangeRecorder board = new(harness);
		await harness.ConnectRepositoryAsync(source);

		// The second card's worker sleeps much longer, so the cards that finish earlier do their git work (merging, removing their sandboxes and branch) while it is still asleep: git can fail for a moment while another git process rewrites the repository's config, and this test is about how many cards run, not about that.
		Agent worker = await harness.AddWorkerAsync("Worker", "--sleep", SleepMilliseconds);
		Agent slowWorker = await harness.AddWorkerAsync("Slow worker", "--sleep", SlowSleepMilliseconds);
		WorkCard first = await harness.AddCardAsync("First", worker);
		WorkCard second = await harness.AddCardAsync("Second", slowWorker);
		WorkCard third = await harness.AddCardAsync("Third", worker);

		await harness.StartPipelineAsync();
		await harness.WaitUntilAsync(async () => (await harness.GetBoardAsync()).Count(card => card.History.IsRunning) == 2, "two cards to be running at once", RuntimeTestTimeouts.Wait);
		BoardCard waiting = (await harness.GetBoardAsync()).Single(card => card.Card.Id == third.Id);
		await harness.WaitForLaneAsync(first.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.WaitForLaneAsync(second.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.WaitForLaneAsync(third.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		board.ThrowIfFailed();
		Assert.Equal(2, board.MaxOpenRounds);
		Assert.Equal((CardLane.Queue, 0), (waiting.Card.Lane, waiting.History.WorkRounds));
		IReadOnlyList<CardRound> firstRounds = await harness.GetHistoryAsync(first.Id);
		IReadOnlyList<CardRound> secondRounds = await harness.GetHistoryAsync(second.Id);
		IReadOnlyList<CardRound> thirdRounds = await harness.GetHistoryAsync(third.Id);
		Assert.True(secondRounds[0].StartedUtc < firstRounds[0].EndedUtc, "The second card started while the first was still working.");
		Assert.True(thirdRounds[0].StartedUtc >= new[] { firstRounds[^1].EndedUtc, secondRounds[^1].EndedUtc }.Min(), "The third card started before a worker was free.");
	}
}
