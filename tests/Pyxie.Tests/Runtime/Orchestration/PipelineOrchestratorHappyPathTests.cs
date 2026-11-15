using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A card with a worker, a gate, and a reviewer goes through the whole pipeline with real git and fake agents, and its work lands on the target branch. See <see cref="HappyPathScenario"/>.</summary>
public sealed class PipelineOrchestratorHappyPathTests : IClassFixture<HappyPathScenario>
{
	private readonly HappyPathScenario _scenario;

	public PipelineOrchestratorHappyPathTests(HappyPathScenario scenario)
	{
		_scenario = scenario;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void TheCard_MovedThroughEveryLaneInOrderAndCompleted()
	{
		Assert.Equal([CardLane.Queue, CardLane.InProgress, CardLane.Gates, CardLane.Review, CardLane.Complete], _scenario.Lanes);
		Assert.Equal(CardState.Ready, _scenario.Finished.Card.State);
		Assert.False(_scenario.Finished.History.IsRunning, "No round is left running.");
	}

	[Fact]
	public async Task TheCard_HasEveryRoundRecordedInItsHistory()
	{
		IReadOnlyList<CardRound> rounds = await _scenario.Harness.GetHistoryAsync(_scenario.Card.Id);

		Assert.Equal("Work:Completed,Gate:Passed,Review:Approved,Merge:Merged", RoundHistory.Describe(rounds));
		Assert.Equal([1, 1, 1, 1], rounds.Select(round => round.Attempt));
		Assert.Equal(["Worker", null, "Reviewer", null], rounds.Select(round => round.Agent?.Name));
		Assert.Equal("added the feature", rounds[0].Note);
		Assert.Equal("1 gate passed.", rounds[1].Note);
		Assert.Contains("- Check: passed in ", rounds[1].Report, StringComparison.Ordinal);
		Assert.StartsWith("Merged into main as ", rounds[3].Note, StringComparison.Ordinal);
		Assert.All(rounds, round => Assert.NotNull(round.EndedUtc));
	}

	[Fact]
	public async Task TheReviewer_WasBriefedWithTheWorkersHandoffAndTheGateResults()
	{
		IReadOnlyList<CardRound> rounds = await _scenario.Harness.GetHistoryAsync(_scenario.Card.Id);

		Assert.Contains("handoff: feature.txt holds the greeting", rounds[2].Briefing, StringComparison.Ordinal);
		Assert.Contains("- Check: passed in ", rounds[2].Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheClonesMain_HasAMergeCommitOfTheCardBranchOnTopOfItsFormerTipWithTheWorkersFile()
	{
		TempGitRepository repository = _scenario.Clone;

		string[] mergeCommit = (await repository.RunGitAsync("rev-list", "--parents", "-n", "1", "main")).Split(' ');

		Assert.Equal(3, mergeCommit.Length);
		Assert.Equal(_scenario.InitialMain, mergeCommit[1]);
		Assert.Equal("Add the feature", await repository.RunGitAsync("log", "-1", "--format=%s", mergeCommit[2]));
		Assert.Equal($"Merge {_scenario.Branch}: Add the feature", await repository.RunGitAsync("log", "-1", "--format=%s", "main"));
		Assert.Equal("hello", await repository.ReadFileAtAsync("main", "feature.txt"));
		IReadOnlyList<CardRound> rounds = await _scenario.Harness.GetHistoryAsync(_scenario.Card.Id);
		Assert.Contains(mergeCommit[0], rounds[^1].Report, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheSourceFolder_IsLeftWhereItWasBecauseTheCardWasMergedIntoTheClone()
	{
		TempGitRepository source = _scenario.Source;

		Assert.Equal(_scenario.InitialMain, await source.GetHashAsync("main"));
		Assert.Null(await source.ReadFileAtAsync("main", "feature.txt"));
		Assert.False(File.Exists(Path.Combine(source.Folder, "feature.txt")), "The card's file must not appear in the folder that was imported.");
		Assert.False(await source.Git.BranchExistsAsync(source.Folder, _scenario.Branch, Token), "The card's branch exists in the clone only.");
	}

	[Fact]
	public async Task TheCardsSandboxesAndBranch_AreRemovedOnceItIsMerged()
	{
		TempGitRepository repository = _scenario.Clone;

		Assert.False(Directory.Exists(_scenario.WorkerRepository), "The card's worker sandbox must be gone.");
		Assert.False(await repository.Git.BranchExistsAsync(repository.Folder, _scenario.Branch, Token), "The card's branch must be gone.");
		Assert.DoesNotContain(_scenario.Card.Id, await _scenario.Harness.Sandboxes.ListCardsAsync(SandboxNames.ProjectKey(_scenario.Harness.ProjectFolder), Token));
		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(_scenario.Harness.ProjectFolder, _scenario.Card.Id)), "The card's sandbox folders on the host must be gone.");
	}
}
