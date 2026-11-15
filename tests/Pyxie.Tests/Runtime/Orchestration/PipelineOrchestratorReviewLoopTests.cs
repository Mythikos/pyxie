using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A reviewer that rejects sends the card back to its worker with the findings, and the second attempt is merged once the reviewer approves it. See <see cref="ReviewLoopScenario"/>.</summary>
public sealed class PipelineOrchestratorReviewLoopTests : IClassFixture<ReviewLoopScenario>
{
	private readonly ReviewLoopScenario _scenario;

	public PipelineOrchestratorReviewLoopTests(ReviewLoopScenario scenario)
	{
		_scenario = scenario;
	}

	[Fact]
	public async Task TheSourceFolder_KeepsItsOwnMainWhileTheCloneGetsTheMergedWork()
	{
		Assert.Equal(_scenario.InitialMain, await _scenario.Source.GetHashAsync("main"));
		Assert.Null(await _scenario.Source.ReadFileAtAsync("main", "feature.txt"));
	}

	[Fact]
	public void TheRejectedCard_GetsASecondWorkRoundInAttemptTwoThatIsApprovedAndMerged()
	{
		Assert.Equal("Work:Completed,Gate:Passed,Review:Rejected,Work:Completed,Gate:Passed,Review:Approved,Merge:Merged", RoundHistory.Describe(_scenario.Rounds));
		Assert.Equal([1, 1, 1, 2, 2, 2, 2], _scenario.Rounds.Select(round => round.Attempt));
		Assert.Equal(2, _scenario.Finished.Card.Attempt);
		Assert.Equal((2, 2, 2, 1), (_scenario.Finished.History.WorkRounds, _scenario.Finished.History.GateRounds, _scenario.Finished.History.ReviewRounds, _scenario.Finished.History.SendBacks));
		Assert.Equal("add a test", _scenario.Rounds[2].Note);
		Assert.Equal(ReviewLoopScenario.Finding, _scenario.Rounds[2].Report);
	}

	[Fact]
	public void TheWorkersSecondBriefing_QuotesTheRejection()
	{
		CardRound secondWork = _scenario.Rounds.Where(round => round.Kind == RoundKind.Work).ElementAt(1);

		Assert.Contains($"attempt 2 of {PyxieLimits.DefaultAttemptLimit}", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains("Why this card came back", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains("add a test", secondWork.Briefing, StringComparison.Ordinal);
		Assert.Contains(ReviewLoopScenario.Finding, secondWork.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public void TheReviewersSecondBriefing_QuotesThePreviousFindings()
	{
		CardRound secondReview = _scenario.Rounds.Where(round => round.Kind == RoundKind.Review).ElementAt(1);

		Assert.Contains("Previous review findings", secondReview.Briefing, StringComparison.Ordinal);
		Assert.Contains(ReviewLoopScenario.Finding, secondReview.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheClonesMain_GetsTheReworkedFileAndBothWorkRoundsCommits()
	{
		Assert.Equal("with a test", await _scenario.Clone.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal("2", await _scenario.Clone.RunGitAsync("rev-list", "--count", "--no-merges", $"{_scenario.InitialMain}..main"));
		Assert.Equal(2, _scenario.Rounds[^1].Report?.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
	}
}
