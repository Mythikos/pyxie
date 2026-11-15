using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A card whose gate keeps failing gets its attempts and then waits in the Queue, in Error, for a person. The pipeline passes it over while cards behind it run, and runs it again, with a fresh set of attempts, only after it is retried.</summary>
public sealed class PipelineOrchestratorAttemptLimitTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardWhoseGateAlwaysFails_StopsInErrorAfterExactlyItsAttemptLimitOfWorkRounds()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		await harness.AddGateAsync("Check", "--exit", "1", "--outcome", "tests-broke");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Failing feature", worker, attemptLimit: 2);

		await harness.StartPipelineAsync();
		BoardCard stopped = await harness.WaitForStoppedAsync(card.Id, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Failed,Work:Completed,Gate:Failed", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 2, 2], rounds.Select(round => round.Attempt));
		Assert.Equal((CardLane.Queue, CardState.Error), (stopped.Card.Lane, stopped.Card.State));
		Assert.Contains("Stopped after 2 attempts", stopped.History.StopReason, StringComparison.Ordinal);
		Assert.Contains("Gate 'Check' failed with exit code 1.", stopped.History.StopReason, StringComparison.Ordinal);
		Assert.False(stopped.History.IsRunning, "No round is left running on a stopped card.");
		await harness.StopOrchestratorAsync();
		Assert.Null(await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardWhoseGateFails_SendsTheGateOutputToTheWorkerInItsNextBriefing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		await harness.AddGateAsync("Check", "--exit", "1", "--outcome", "tests-broke");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Failing feature", worker, attemptLimit: 2);

		await harness.StartPipelineAsync();
		await harness.WaitForStoppedAsync(card.Id, RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Contains("Gate 'Check' failed with exit code 1.", rounds[1].Note, StringComparison.Ordinal);
		Assert.Contains("fake harness: tests-broke", rounds[1].Report, StringComparison.Ordinal);
		Assert.Contains("attempt 2 of 2", rounds[2].Briefing, StringComparison.Ordinal);
		Assert.Contains("Why this card came back", rounds[2].Briefing, StringComparison.Ordinal);
		Assert.Contains("fake harness: tests-broke", rounds[2].Briefing, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AStoppedCard_IsPassedOverWhileCardsBehindItRunAndIsRunAgainWithFreshAttemptsAfterARetry()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		await harness.AddGateAsync("Check", "--exit", "1");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent unclearWorker = await harness.AddWorkerAsync("Unclear worker", "--outcome", "blocked", "--note", "the task is unclear");
		WorkCard failing = await harness.AddCardAsync("Failing feature", worker, attemptLimit: 2);
		WorkCard behind = await harness.AddCardAsync("Behind the failing card", unclearWorker);

		await harness.StartPipelineAsync();
		await harness.WaitForStoppedAsync(failing.Id, RuntimeTestTimeouts.Wait);
		await harness.WaitForStoppedAsync(behind.Id, RuntimeTestTimeouts.Wait);

		// The card behind it ran to its own stop while the failing card sat above it in Error, so the pipeline looked at the Queue and passed over the stopped card.
		IReadOnlyList<CardRound> beforeRetry = await harness.GetHistoryAsync(failing.Id);
		Assert.Equal("Work:Completed,Gate:Failed,Work:Completed,Gate:Failed", RoundHistory.Describe(beforeRetry));
		Assert.Equal("Work:Blocked", RoundHistory.Describe(await harness.GetHistoryAsync(behind.Id)));

		await using (AsyncServiceScope scope = harness.CreateScope())
		{
			ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().RetryAsync(failing.Id, Token));
		}

		harness.NotifyCardsChanged();
		BoardCard stoppedAgain = await harness.WaitForCardAsync(failing.Id, card => card.Card.State == CardState.Error && card.History.WorkRounds == 4, $"card {failing.Id} to run again and stop again", RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> afterRetry = await harness.GetHistoryAsync(failing.Id);
		Assert.Equal("Work:Completed,Gate:Failed,Work:Completed,Gate:Failed,Work:Completed,Gate:Failed,Work:Completed,Gate:Failed", RoundHistory.Describe(afterRetry));
		Assert.Equal([1, 1, 2, 2, 1, 1, 2, 2], afterRetry.Select(round => round.Attempt));
		Assert.Contains("Stopped after 2 attempts", stoppedAgain.History.StopReason, StringComparison.Ordinal);
		Assert.Equal("Work:Blocked", RoundHistory.Describe(await harness.GetHistoryAsync(behind.Id)));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ARetriedCard_ThatThenPasses_IsMergedLikeAnyOther()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);

		// The gate fails its first two runs, which are the card's two attempts, and passes from then on.
		string gateScript = await harness.WriteScriptAsync(["--exit", "1"], ["--exit", "1"], ["--exit", "0"]);
		await harness.AddGateAsync(new GateInput(
			"Flaky check",
			FakeHarnessAgent.Command,
			FakeHarnessAgent.ScriptArguments(HarnessKind.Other, gateScript),
			TimeoutMinutes: 5,
			IsEnabled: true));
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Flaky feature", worker, attemptLimit: 2);

		await harness.StartPipelineAsync();
		await harness.WaitForStoppedAsync(card.Id, RuntimeTestTimeouts.Wait);
		await using (AsyncServiceScope scope = harness.CreateScope())
		{
			ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().RetryAsync(card.Id, Token));
		}

		harness.NotifyCardsChanged();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Failed,Work:Completed,Gate:Failed,Work:Completed,Gate:Passed,Review:Skipped,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}
}
