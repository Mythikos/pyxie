using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Two cards that replace the one line of <c>README.md</c>, run at once by two workers. Both workers sleep before they write, so both cards have their branch from the same <c>main</c> before either is merged; the first card's worker sleeps less, so it is merged long before the second card (the slow one) reaches its merge, and the second is the one that conflicts. The slow worker's second run does not sleep, so the second card uses its two attempts quickly and stops. The tests of <see cref="PipelineOrchestratorConflictTests"/> look at the finished run.</summary>
public sealed class ConflictScenario : IAsyncLifetime
{
	private const string FastSleepMilliseconds = "2500";

	// The first card's whole run after its worker is done has to fit in the gap between the two sleeps.
	private const string SlowSleepMilliseconds = "9000";

	private TempGitRepository? _repository;
	private RuntimeHarness? _harness;

	/// <summary>Gets the repository that was imported. The pipeline never changes it.</summary>
	public TempGitRepository Source => _repository ?? throw new InvalidOperationException("The scenario has not run.");

	/// <summary>Gets Pyxie's clone of the source, which both cards were merged into, or tried to be.</summary>
	public TempGitRepository Clone => Harness.Clone;

	/// <summary>Gets the runtime the cards ran in.</summary>
	public RuntimeHarness Harness => _harness ?? throw new InvalidOperationException("The scenario has not run.");

	/// <summary>Gets the connected repository, whose path is Pyxie's clone.</summary>
	public Repository Connected { get; private set; } = null!;

	/// <summary>Gets the card that was merged.</summary>
	public BoardCard Winner { get; private set; } = null!;

	/// <summary>Gets the card that conflicted and stopped.</summary>
	public BoardCard Loser { get; private set; } = null!;

	/// <summary>Gets the winner's rounds, oldest first.</summary>
	public IReadOnlyList<CardRound> WinnerRounds { get; private set; } = [];

	/// <summary>Gets the loser's rounds, oldest first.</summary>
	public IReadOnlyList<CardRound> LoserRounds { get; private set; } = [];

	/// <inheritdoc/>
	public async ValueTask InitializeAsync()
	{
		_repository = await TempGitRepository.CreateAsync();
		_harness = new RuntimeHarness(workers: 2, roundTimeoutMinutes: 1);
		Connected = await _harness.ConnectRepositoryAsync(_repository);
		string fast = await _harness.WriteScriptAsync(["--sleep", FastSleepMilliseconds, "--write", "README.md=from the first card"], ["--write", "README.md=from the first card"]);
		string slow = await _harness.WriteScriptAsync(["--sleep", SlowSleepMilliseconds, "--write", "README.md=from the second card"], ["--write", "README.md=from the second card"]);
		Agent fastWorker = await _harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Fast worker", HarnessKind.Other, canWork: true, canReview: false, fast));
		Agent slowWorker = await _harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Slow worker", HarnessKind.Other, canWork: true, canReview: false, slow));
		WorkCard first = await _harness.AddCardAsync("First card", fastWorker, attemptLimit: 2);
		WorkCard second = await _harness.AddCardAsync("Second card", slowWorker, attemptLimit: 2);

		await _harness.StartPipelineAsync();
		Winner = await _harness.WaitForLaneAsync(first.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		Loser = await _harness.WaitForStoppedAsync(second.Id, RuntimeTestTimeouts.Wait);

		// Shutting the orchestrator down waits for the merged card's workspace to be removed and leaves git quiet: git can fail for a moment while another git process rewrites the repository's config.
		await _harness.StopOrchestratorAsync();
		WinnerRounds = await _harness.GetHistoryAsync(first.Id);
		LoserRounds = await _harness.GetHistoryAsync(second.Id);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		if (_harness is not null)
		{
			await _harness.DisposeAsync();
		}

		if (_repository is not null)
		{
			await _repository.DisposeAsync();
		}
	}
}
