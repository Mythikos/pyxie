using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>One card with a worker, a passing gate, and an approving reviewer, run through the real pipeline once. The tests of <see cref="PipelineOrchestratorHappyPathTests"/> each look at one aspect of the same finished run, and the card's sandbox and branch are already removed when they start. The repository and the runtime stay alive for them to query.</summary>
public sealed class HappyPathScenario : IAsyncLifetime
{
	private TempGitRepository? _repository;
	private RuntimeHarness? _harness;

	/// <summary>Gets the repository that was imported. The pipeline never changes it.</summary>
	public TempGitRepository Source => _repository ?? throw new InvalidOperationException("The scenario has not run.");

	/// <summary>Gets Pyxie's clone of the source, which the card was merged into.</summary>
	public TempGitRepository Clone => Harness.Clone;

	/// <summary>Gets the runtime the card ran in.</summary>
	public RuntimeHarness Harness => _harness ?? throw new InvalidOperationException("The scenario has not run.");

	/// <summary>Gets the card.</summary>
	public WorkCard Card { get; private set; } = null!;

	/// <summary>Gets the card as it stood once it was complete.</summary>
	public BoardCard Finished { get; private set; } = null!;

	/// <summary>Gets the lanes the card was seen in, in order.</summary>
	public IReadOnlyList<CardLane> Lanes { get; private set; } = [];

	/// <summary>Gets the commit <c>main</c> pointed at, in the source and in the clone, before the card was merged.</summary>
	public string InitialMain { get; private set; } = string.Empty;

	/// <summary>Gets the folder that stood for the card's worker sandbox's repository.</summary>
	public string WorkerRepository { get; private set; } = string.Empty;

	/// <summary>Gets the card's branch name.</summary>
	public string Branch { get; private set; } = string.Empty;

	/// <inheritdoc/>
	public async ValueTask InitializeAsync()
	{
		_repository = await TempGitRepository.CreateAsync();
		_harness = new RuntimeHarness(roundTimeoutMinutes: 1);
		using BoardChangeRecorder board = new(_harness);
		InitialMain = await _repository.GetHashAsync("main");
		Repository connected = await _harness.ConnectRepositoryAsync(_repository);
		await _harness.AddGateAsync("Check", "--exit", "0");
		Agent worker = await _harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello", "--note", "added the feature", "--report", "handoff: feature.txt holds the greeting");
		Agent reviewer = await _harness.AddReviewerAsync("Reviewer", "--outcome", "approved");
		Card = await _harness.AddCardAsync("Add the feature", worker, reviewer);
		WorkerRepository = _harness.WorkerRepository(Card.Id);
		Branch = $"pyxie/card-{Card.Id}";

		await _harness.StartPipelineAsync();
		Finished = await _harness.WaitForLaneAsync(Card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		// The recorder sees a change when the pipeline announces it, which is a moment after the database shows it, and the workspace is removed right after that. Shutting the orchestrator down waits for that removal, and leaves git quiet for the tests: git can fail for a moment while another git process rewrites the repository's config.
		await _harness.WaitUntilAsync(() => Task.FromResult(board.LanesOf(Card.Id).LastOrDefault() == CardLane.Complete), $"the board recorder to see card {Card.Id} complete", RuntimeTestTimeouts.Wait);
		await _harness.StopOrchestratorAsync();
		board.ThrowIfFailed();
		Lanes = board.LanesOf(Card.Id);
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
