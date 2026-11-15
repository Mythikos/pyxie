using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Two cards change the same line of the same file and the project has a merge agent: the first to be merged wins, and the second is not sent back to its worker. The merge agent resolves the conflict in a Resolve round, which uses an attempt, and because the resolution is new code written by an agent, the gates and the reviewer check it before Pyxie merges it. See <see cref="ResolveScenario"/>.</summary>
public sealed class PipelineOrchestratorResolveTests : IClassFixture<ResolveScenario>
{
	private readonly ResolveScenario _scenario;

	public PipelineOrchestratorResolveTests(ResolveScenario scenario)
	{
		_scenario = scenario;
	}

	private CardRound Round(RoundKind kind, RoundOutcome outcome)
	{
		return _scenario.LoserRounds.Single(round => round.Kind == kind && round.Outcome == outcome);
	}

	[Fact]
	public void TheFirstCardToBeMerged_RunsThroughTheGateAndTheReviewAndIsMerged()
	{
		Assert.Equal("Work:Completed,Gate:Passed,Review:Approved,Merge:Merged", RoundHistory.Describe(_scenario.WinnerRounds));
	}

	[Fact]
	public void TheSecondCard_IsResolvedByTheMergeAgentAndRunsThroughTheGateAndTheReviewAgainBeforeItIsMerged()
	{
		Assert.Equal("Work:Completed,Gate:Passed,Review:Approved,Merge:Conflicted,Resolve:Resolved,Gate:Passed,Review:Approved,Merge:Merged", RoundHistory.Describe(_scenario.LoserRounds));
		Assert.Equal([1, 1, 1, 1, 2, 2, 2, 2], _scenario.LoserRounds.Select(round => round.Attempt));
		Assert.Equal((CardLane.Complete, 2), (_scenario.Loser.Card.Lane, _scenario.Loser.Card.Attempt));
	}

	[Fact]
	public void TheResolveRound_RanTheMergeAgentAndKeptItsNoteAndItsReportWithHowTheMergeResolvedTheConflict()
	{
		CardRound resolve = Round(RoundKind.Resolve, RoundOutcome.Resolved);

		Assert.Equal("Merger", resolve.Agent?.Name);
		Assert.Equal(ResolveScenario.ResolveNote, resolve.Note);
		string report = resolve.Report?.ReplaceLineEndings("\n") ?? string.Empty;
		Assert.StartsWith($"{ResolveScenario.ResolveReport}\n\n## Resolution\n\nMerge commit ", report, StringComparison.Ordinal);
		Assert.Contains("```diff", report, StringComparison.Ordinal);
		Assert.Contains($"+{ResolveScenario.ResolvedContent}", report, StringComparison.Ordinal);
	}

	[Fact]
	public void TheConflictedMergeRound_NamesTheFileAndWhatLandedWithTheTaskOfTheCardThatLandedIt()
	{
		CardRound conflicted = Round(RoundKind.Merge, RoundOutcome.Conflicted);
		string report = conflicted.Report?.ReplaceLineEndings("\n") ?? string.Empty;

		Assert.Equal($"Merging main into pyxie/card-{_scenario.Loser.Card.Id} conflicts in 1 file.", conflicted.Note);
		Assert.StartsWith("Conflicting files:\n\n- README.md\n\nLanded on main since this branch last took it in, newest first:\n\n- ", report, StringComparison.Ordinal);
		Assert.Contains($" Merge pyxie/card-{_scenario.Winner.Card.Id}: First card\n  Its task:\n  > {ResolveScenario.FirstDescription}", report, StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", report, StringComparison.Ordinal);
	}

	[Fact]
	public void TheMergeAgentsBriefing_SaysWhyTheMergeStoppedWithWhatLandedAndAsksForTheResolution()
	{
		string briefing = Round(RoundKind.Resolve, RoundOutcome.Resolved).Briefing ?? string.Empty;

		Assert.Contains($"attempt 2 of {PyxieLimits.DefaultAttemptLimit}", briefing, StringComparison.Ordinal);
		Assert.Contains("Pyxie has started merging `main`", briefing, StringComparison.Ordinal);
		Assert.Contains("Why the merge stopped", briefing, StringComparison.Ordinal);
		Assert.Contains("- README.md", briefing, StringComparison.Ordinal);
		Assert.Contains($"Merge pyxie/card-{_scenario.Winner.Card.Id}: First card", briefing, StringComparison.Ordinal);
		Assert.Contains(ResolveScenario.FirstDescription, briefing, StringComparison.Ordinal);
		Assert.Contains("The card's work", briefing, StringComparison.Ordinal);
		Assert.Contains("\"outcome\": \"resolved\"", briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void TheSecondCardsSecondReview_IsToReviewTheMergeTheMergeAgentResolved()
	{
		CardRound secondReview = _scenario.LoserRounds.Where(round => round.Kind == RoundKind.Review).ElementAt(1);

		Assert.Contains("Merge to review", secondReview.Briefing, StringComparison.Ordinal);
		Assert.Contains("The resolution", secondReview.Briefing, StringComparison.Ordinal);
		Assert.Contains(ResolveScenario.ResolveNote, secondReview.Briefing, StringComparison.Ordinal);
		Assert.Contains("git show --remerge-diff HEAD", secondReview.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheClonesMain_HasBothCardsChangesAndTheMergeAgentsResolutionAsTheSecondMergeCommit()
	{
		TempGitRepository clone = _scenario.Clone;
		int first = _scenario.Winner.Card.Id;
		int second = _scenario.Loser.Card.Id;

		Assert.Equal(ResolveScenario.ResolvedContent, await clone.ReadFileAtAsync("main", "README.md"));
		Assert.Equal("first", await clone.ReadFileAtAsync("main", "first.txt"));
		Assert.Equal("second", await clone.ReadFileAtAsync("main", "second.txt"));
		Assert.Equal([$"Merge pyxie/card-{second}: Second card", $"Merge pyxie/card-{first}: First card", "Initial commit"], (await clone.RunGitAsync("log", "--first-parent", "--format=%s", "main")).ReplaceLineEndings("\n").Split('\n'));

		string firstMerge = await clone.GetHashAsync("main^1");
		Assert.Equal(_scenario.InitialMain, await clone.GetHashAsync($"{firstMerge}^1"));

		// The second card's branch ends in the merge commit the merge agent made: the card's work and the first card's merge.
		string[] resolutionParents = (await clone.RunGitAsync("log", "-1", "--format=%P", "main^2")).Split(' ');
		Assert.Equal(2, resolutionParents.Length);
		Assert.Equal(firstMerge, resolutionParents[1]);
		Assert.Equal("Merge main into pyxie/card-" + second, await clone.RunGitAsync("log", "-1", "--format=%s", "main^2"));
	}

	[Fact]
	public async Task TheSourceFolder_NeverGetsEitherCardsWorkOrTheResolution()
	{
		TempGitRepository source = _scenario.Source;

		Assert.Equal(_scenario.InitialMain, await source.GetHashAsync("main"));
		Assert.Equal(TempGitRepository.InitialContent, await source.ReadFileAtAsync("main", "README.md"));
	}

	[Fact]
	public void TheResolveRound_RanInASandboxOfItsOwnThatIsGoneWithTheRestOfTheCardsWorkspace()
	{
		int second = _scenario.Loser.Card.Id;
		string key = SandboxNames.ProjectKey(_scenario.Harness.ProjectFolder);

		SandboxSpec resolve = Assert.Single(_scenario.Harness.Sandboxes.Started, spec => spec.Role == SandboxRole.Resolve && spec.CardId == second);
		Assert.Equal((SandboxNames.Container(key, second, SandboxRole.Resolve), false), (resolve.Name, resolve.Keep));
		Assert.Empty(resolve.Volumes);
		Assert.False(Directory.Exists(_scenario.Harness.Sandboxes.WorkFolder(resolve.Name)), "The resolve sandbox goes when its round ends.");
		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(_scenario.Harness.ProjectFolder, second)), "A merged card leaves nothing of its sandboxes on the host.");
	}
}
