using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Two cards that replace the one line of <c>README.md</c>, run at once by two workers, in a project with a merge agent, a gate, and a reviewer. The second card's worker waits for a file that the test creates once the first card has been merged, so the second card's branch is certain to start from the <c>main</c> the first card then changes, and the second card conflicts. The merge agent resolves it, and the gate and the reviewer check the resolution before the second card is merged. The tests of <see cref="PipelineOrchestratorResolveTests"/> look at the finished run.</summary>
public sealed class ResolveScenario : IAsyncLifetime
{
	/// <summary>The task of the first card, which the merge agent is told about because it landed what the second card conflicts with.</summary>
	public const string FirstDescription = "Make the readme say the first thing.";

	/// <summary>The line the merge agent writes into the conflicting file.</summary>
	public const string ResolvedContent = "from both cards";

	/// <summary>What the merge agent says in its note.</summary>
	public const string ResolveNote = "kept both lines";

	/// <summary>What the merge agent says in its report.</summary>
	public const string ResolveReport = "Kept the first card's line and the second card's.";

	private static readonly TimeSpan s_secondCardWait = RuntimeTestTimeouts.Wait * 2;

	private TempGitRepository? _repository;
	private RuntimeHarness? _harness;

	/// <summary>Gets the repository that was imported. The pipeline never changes it.</summary>
	public TempGitRepository Source => _repository ?? throw new InvalidOperationException("The scenario has not run.");

	/// <summary>Gets Pyxie's clone of the source, which both cards were merged into.</summary>
	public TempGitRepository Clone => Harness.Clone;

	/// <summary>Gets the runtime the cards ran in.</summary>
	public RuntimeHarness Harness => _harness ?? throw new InvalidOperationException("The scenario has not run.");

	/// <summary>Gets the card that was merged first.</summary>
	public BoardCard Winner { get; private set; } = null!;

	/// <summary>Gets the card that conflicted, was resolved by the merge agent, and was merged second.</summary>
	public BoardCard Loser { get; private set; } = null!;

	/// <summary>Gets the first card's rounds, oldest first.</summary>
	public IReadOnlyList<CardRound> WinnerRounds { get; private set; } = [];

	/// <summary>Gets the second card's rounds, oldest first.</summary>
	public IReadOnlyList<CardRound> LoserRounds { get; private set; } = [];

	/// <summary>Gets the commit <c>main</c> pointed at, in the source and in the clone, before either card was merged.</summary>
	public string InitialMain { get; private set; } = string.Empty;

	/// <inheritdoc/>
	public async ValueTask InitializeAsync()
	{
		_repository = await TempGitRepository.CreateAsync();
		_harness = new RuntimeHarness(workers: 2, roundTimeoutMinutes: 2);
		InitialMain = await _repository.GetHashAsync("main");
		await _harness.ConnectRepositoryAsync(_repository);
		string release = Path.Combine(_harness.ScriptsFolder, "second-card-may-write");

		// Every agent has plain arguments, never a script: a script counts its runs in a file, which two workers at once would race for.
		await _harness.AddGateAsync("Check", "--exit", "0");
		Agent firstWorker = await _harness.AddWorkerAsync("First worker", "--write", "README.md=from the first card", "--write", "first.txt=first");
		Agent secondWorker = await _harness.AddWorkerAsync("Second worker", "--wait-for", release, "--write", "README.md=from the second card", "--write", "second.txt=second");
		Agent reviewer = await _harness.AddReviewerAsync("Reviewer", "--outcome", "approved");
		await _harness.AddMergerAsync("Merger", "--write", $"README.md={ResolvedContent}", "--outcome", "resolved", "--note", ResolveNote, "--report", ResolveReport);
		WorkCard first = await _harness.AddCardAsync("First card", firstWorker, reviewer, FirstDescription);
		WorkCard second = await _harness.AddCardAsync("Second card", secondWorker, reviewer, "Make the readme say the second thing.");

		await _harness.StartPipelineAsync();
		Winner = await _harness.WaitForLaneAsync(first.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await File.WriteAllTextAsync(release, "go", TestContext.Current.CancellationToken);
		Loser = await _harness.WaitForLaneAsync(second.Id, CardLane.Complete, s_secondCardWait);

		// Shutting the orchestrator down waits for the merged cards' workspaces to be removed and leaves git quiet: git can fail for a moment while another git process rewrites the repository's config.
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
