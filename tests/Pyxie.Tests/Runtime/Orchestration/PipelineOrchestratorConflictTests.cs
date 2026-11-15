using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Two cards that change the same line of the same file: the first to be merged wins, and the second is sent back with the conflicting file named. See <see cref="ConflictScenario"/>.</summary>
public sealed class PipelineOrchestratorConflictTests : IClassFixture<ConflictScenario>
{
	private readonly ConflictScenario _scenario;

	public PipelineOrchestratorConflictTests(ConflictScenario scenario)
	{
		_scenario = scenario;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheFirstCardToBeMerged_LandsItsLineOnTheClonesMain()
	{
		Assert.Equal("Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(_scenario.WinnerRounds));
		Assert.Equal("from the first card", await _scenario.Clone.ReadFileAtAsync("main", "README.md"));
	}

	[Fact]
	public async Task TheSourceFolder_NeverGetsEitherCardsLineOrTheLosingCardsBranch()
	{
		TempGitRepository source = _scenario.Source;

		Assert.Equal(TempGitRepository.InitialContent, await source.ReadFileAtAsync("main", "README.md"));
		Assert.Equal(TempGitRepository.InitialContent, await File.ReadAllTextAsync(Path.Combine(source.Folder, "README.md"), Token));
		Assert.False(await source.Git.BranchExistsAsync(source.Folder, $"pyxie/card-{_scenario.Loser.Card.Id}", Token), "The losing card's branch exists in the clone only.");
	}

	[Fact]
	public void TheSecondCard_IsSentBackWithAConflictedMergeRoundAndThenConflictsAgain()
	{
		Assert.Equal(
			"Work:Completed,Gate:Skipped,Review:Skipped,Merge:Conflicted,Work:Completed,Gate:Skipped,Review:Skipped,Merge:Conflicted",
			RoundHistory.Describe(_scenario.LoserRounds));
		Assert.Equal([1, 1, 1, 1, 2, 2, 2, 2], _scenario.LoserRounds.Select(round => round.Attempt));
	}

	[Fact]
	public void TheSecondCard_IsToldWhichFileConflictsInTheNoteAndTheReport()
	{
		CardRound conflicted = _scenario.LoserRounds.First(round => round.Outcome == RoundOutcome.Conflicted);

		Assert.Equal($"Merging main into pyxie/card-{_scenario.Loser.Card.Id} conflicts in 1 file.", conflicted.Note);
		Assert.Contains("- README.md", conflicted.Report, StringComparison.Ordinal);
	}

	[Fact]
	public void TheSecondCardsNextWorkBriefing_NamesTheConflictingFileAndHowToResolveIt()
	{
		CardRound secondWork = _scenario.LoserRounds.Where(round => round.Kind == RoundKind.Work).ElementAt(1);

		Assert.Contains("attempt 2 of 2", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains("Why this card came back", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains("conflicts in 1 file", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains("- README.md", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains("git merge main", secondWork.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheSecondCard_StopsInErrorOnItsLastAttemptAndLeavesItsWorkerSandboxOnItsOwnWork()
	{
		BoardCard loser = _scenario.Loser;
		string workerRepository = _scenario.Harness.WorkerRepository(loser.Card.Id);

		Assert.Equal((CardLane.Queue, CardState.Error), (loser.Card.Lane, loser.Card.State));
		Assert.Contains("Stopped after 2 attempts", loser.History.StopReason, StringComparison.Ordinal);
		Assert.Contains("conflicts in 1 file", loser.History.StopReason, StringComparison.Ordinal);
		Assert.Empty(await WorkingTreeStatus.ListAsync(workerRepository));
		Assert.Equal("from the second card", await File.ReadAllTextAsync(Path.Combine(workerRepository, "README.md"), Token));
		Assert.Equal("from the second card", await _scenario.Clone.ReadFileAtAsync($"pyxie/card-{loser.Card.Id}", "README.md"));
	}
}
