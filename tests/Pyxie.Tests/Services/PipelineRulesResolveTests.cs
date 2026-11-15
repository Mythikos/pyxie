using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>The merge agent's part of the state machine in docs/pipeline.md: a conflict goes to a Resolve round when the project has a merge agent and to the worker when it has none, a resolution goes through the gates and the review again, and a stage that names a removed merge agent falls back to the worker.</summary>
public sealed class PipelineRulesResolveTests
{
	private static CardProgress Progress(int attempt = 1, int limit = 3, bool hasMerger = true, bool approved = false)
	{
		return new CardProgress(attempt, limit, HasGates: true, HasReviewer: true, hasMerger, approved);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void Next_AfterAConflictWithAMergeAgent_OpensAResolveRoundOnTheNextAttempt(int attempt)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Resolve, attempt + 1), PipelineRules.Next(RoundKind.Merge, RoundOutcome.Conflicted, Progress(attempt, limit: 3, hasMerger: true)));
	}

	[Theory]
	[InlineData(3, 3)]
	[InlineData(1, 1)]
	[InlineData(4, 3)]
	public void Next_AfterAConflictWithAMergeAgentOnTheLastAttempt_StopsTheCardAndResumesAtResolve(int attempt, int limit)
	{
		Assert.Equal(PipelineStep.Stop(RoundKind.Resolve, attempt), PipelineRules.Next(RoundKind.Merge, RoundOutcome.Conflicted, Progress(attempt, limit, hasMerger: true)));
	}

	[Theory]
	[InlineData(true, RoundKind.Resolve)]
	[InlineData(false, RoundKind.Work)]
	public void Next_AfterAConflict_GoesToTheMergeAgentWhenTheProjectHasOneAndToTheWorkerWhenItHasNone(bool hasMerger, RoundKind expected)
	{
		Assert.Equal(PipelineStep.Open(expected, 2), PipelineRules.Next(RoundKind.Merge, RoundOutcome.Conflicted, Progress(attempt: 1, hasMerger: hasMerger)));
	}

	[Theory]
	[InlineData(1, false, true)]
	[InlineData(2, true, true)]
	[InlineData(3, false, false)]
	[InlineData(3, true, false)]
	public void Next_AfterAResolution_OpensTheGatesOnTheSameAttemptWhateverEarlierApprovalsSay(int attempt, bool approved, bool hasMerger)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Gate, attempt), PipelineRules.Next(RoundKind.Resolve, RoundOutcome.Resolved, Progress(attempt, limit: 3, hasMerger, approved)));
	}

	[Fact]
	public void Next_AfterAResolutionIsGated_OpensTheReviewBecauseTheResolutionChangedTheApprovedWork()
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted),
			TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved),
			TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed),
		];
		CardProgress progress = Progress(attempt: 2, approved: PipelineRules.IsApprovedSinceLastWork(rounds));

		Assert.Equal(PipelineStep.Open(RoundKind.Review, 2), PipelineRules.Next(RoundKind.Gate, RoundOutcome.Passed, progress));
	}

	[Fact]
	public void Next_AfterTheResolutionIsApproved_OpensTheMergeAgain()
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(3, RoundKind.Merge, RoundOutcome.Conflicted),
			TestRound.Create(4, RoundKind.Resolve, RoundOutcome.Resolved),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(6, RoundKind.Review, RoundOutcome.Approved),
		];

		Assert.Equal(PipelineStep.Open(RoundKind.Merge, 2), PipelineRules.Next(RoundKind.Review, RoundOutcome.Approved, Progress(attempt: 2, approved: PipelineRules.IsApprovedSinceLastWork(rounds))));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void KindToOpen_OfAResolveStage_IsAResolveRoundOnlyWhileTheProjectHasAMergeAgent(bool hasMerger)
	{
		RoundKind expected = hasMerger ? RoundKind.Resolve : RoundKind.Work;

		Assert.Equal(expected, PipelineRules.KindToOpen(RoundKind.Resolve, Progress(hasMerger: hasMerger)));
	}

	[Theory]
	[InlineData(RoundKind.Work, true)]
	[InlineData(RoundKind.Work, false)]
	[InlineData(RoundKind.Gate, true)]
	[InlineData(RoundKind.Gate, false)]
	[InlineData(RoundKind.Review, true)]
	[InlineData(RoundKind.Review, false)]
	[InlineData(RoundKind.Merge, true)]
	[InlineData(RoundKind.Merge, false)]
	public void KindToOpen_OfAnyOtherStage_IsThatStageWhateverTheProjectHas(RoundKind kind, bool hasMerger)
	{
		Assert.Equal(kind, PipelineRules.KindToOpen(kind, Progress(hasMerger: hasMerger)));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void SkipReason_OfAResolveRound_IsNullBecauseTheStageAlwaysApplies(bool hasMerger)
	{
		Assert.Null(PipelineRules.SkipReason(RoundKind.Resolve, new CardProgress(1, 3, HasGates: false, HasReviewer: false, hasMerger, ApprovedSinceLastWork: false)));
	}

	[Theory]
	[InlineData(RoundOutcome.Resolved)]
	[InlineData(RoundOutcome.Errored)]
	[InlineData(RoundOutcome.Blocked)]
	[InlineData(RoundOutcome.Interrupted)]
	public void IsApprovedSinceLastWork_IsFalseOnceAResolveRoundRan_WhateverItsOutcome(RoundOutcome resolve)
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted),
			TestRound.Create(5, RoundKind.Resolve, resolve),
			TestRound.Create(6, RoundKind.Gate, RoundOutcome.Passed),
		];

		Assert.False(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Fact]
	public void IsApprovedSinceLastWork_IsTrueAgainWhenAReviewApprovesTheResolution()
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(3, RoundKind.Merge, RoundOutcome.Conflicted),
			TestRound.Create(4, RoundKind.Resolve, RoundOutcome.Resolved),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(6, RoundKind.Review, RoundOutcome.Approved),
		];

		Assert.True(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Fact]
	public void IsApprovedSinceLastWork_KeepsTheApprovalThroughAnOutdatedMergeButNotThroughAResolution()
	{
		CardRound[] outdated =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(3, RoundKind.Merge, RoundOutcome.Outdated),
			TestRound.Create(4, RoundKind.Gate, RoundOutcome.Passed),
		];
		CardRound[] resolved =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(3, RoundKind.Merge, RoundOutcome.Conflicted),
			TestRound.Create(4, RoundKind.Resolve, RoundOutcome.Resolved),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed),
		];

		Assert.True(PipelineRules.IsApprovedSinceLastWork(outdated), "Pyxie's own clean merge of a moved target keeps the approval; the gates check it.");
		Assert.False(PipelineRules.IsApprovedSinceLastWork(resolved), "A resolution is new agent-written code and must be reviewed.");
	}

	[Fact]
	public void IsApprovedSinceLastWork_OfAResolutionWithoutAReviewerStaysTrueOnlyThroughTheSkippedReviewThatFollowsIt()
	{
		CardRound[] unreviewed =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Skipped),
			TestRound.Create(3, RoundKind.Merge, RoundOutcome.Conflicted),
			TestRound.Create(4, RoundKind.Resolve, RoundOutcome.Resolved),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed),
		];
		CardRound[] skipped = [.. unreviewed, TestRound.Create(6, RoundKind.Review, RoundOutcome.Skipped)];

		Assert.False(PipelineRules.IsApprovedSinceLastWork(unreviewed));
		Assert.True(PipelineRules.IsApprovedSinceLastWork(skipped));
	}
}
