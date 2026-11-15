using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>One card whose reviewer rejects the first attempt and approves the second, run through the real pipeline once. The worker changes its file differently on each run, and a passing gate stands between work and review. The tests of <see cref="PipelineOrchestratorReviewLoopTests"/> look at the finished run.</summary>
public sealed class ReviewLoopScenario : IAsyncLifetime
{
	/// <summary>The findings the reviewer writes when it rejects the first attempt.</summary>
	public const string Finding = "Finding: the feature has no test";

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

	/// <summary>Gets the card's rounds, oldest first.</summary>
	public IReadOnlyList<CardRound> Rounds { get; private set; } = [];

	/// <summary>Gets the commit <c>main</c> pointed at, in the source and in the clone, before the card was merged.</summary>
	public string InitialMain { get; private set; } = string.Empty;

	/// <inheritdoc/>
	public async ValueTask InitializeAsync()
	{
		_repository = await TempGitRepository.CreateAsync();
		_harness = new RuntimeHarness(roundTimeoutMinutes: 1);
		InitialMain = await _repository.GetHashAsync("main");
		await _harness.ConnectRepositoryAsync(_repository);
		await _harness.AddGateAsync("Check", "--exit", "0");
		string workerScript = await _harness.WriteScriptAsync(["--write", "feature.txt=first try", "--note", "first try"], ["--write", "feature.txt=with a test", "--note", "added the test"]);
		string reviewerScript = await _harness.WriteScriptAsync(["--outcome", "rejected", "--note", "add a test", "--report", Finding], ["--outcome", "approved"]);
		Agent worker = await _harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Worker", HarnessKind.Other, canWork: true, canReview: false, workerScript));
		Agent reviewer = await _harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Reviewer", HarnessKind.Other, canWork: false, canReview: true, reviewerScript));
		Card = await _harness.AddCardAsync("Add the feature", worker, reviewer);

		await _harness.StartPipelineAsync();
		Finished = await _harness.WaitForLaneAsync(Card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		// Shutting the orchestrator down waits for the merged card's workspace to be removed and leaves git quiet: git can fail for a moment while another git process rewrites the repository's config.
		await _harness.StopOrchestratorAsync();
		Rounds = await _harness.GetHistoryAsync(Card.Id);
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
