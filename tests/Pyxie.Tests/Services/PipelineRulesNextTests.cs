using Pyxie.Data.Enums;
using Pyxie.Data.Services.Rules;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>What follows a finished round, row by row from the state machine in docs/pipeline.md, including the setback that uses the last attempt.</summary>
public sealed class PipelineRulesNextTests
{
	private static CardProgress Progress(int attempt = 1, int limit = 3, bool hasGates = true, bool hasReviewer = true, bool approved = false, bool hasMerger = false)
	{
		return new CardProgress(attempt, limit, hasGates, hasReviewer, hasMerger, approved);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void Next_AfterCompletedWork_OpensTheGatesOnTheSameAttempt(int attempt)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Gate, attempt), PipelineRules.Next(RoundKind.Work, RoundOutcome.Completed, Progress(attempt)));
	}

	[Theory]
	[InlineData(RoundOutcome.Passed)]
	[InlineData(RoundOutcome.Skipped)]
	public void Next_AfterPassedOrSkippedGates_OpensTheReviewWhenTheWorkWasNotApprovedYet(RoundOutcome outcome)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Review, 2), PipelineRules.Next(RoundKind.Gate, outcome, Progress(attempt: 2, approved: false)));
	}

	[Theory]
	[InlineData(RoundOutcome.Passed)]
	[InlineData(RoundOutcome.Skipped)]
	public void Next_AfterPassedOrSkippedGates_OpensTheMergeWhenTheCurrentWorkWasAlreadyApproved(RoundOutcome outcome)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Merge, 2), PipelineRules.Next(RoundKind.Gate, outcome, Progress(attempt: 2, approved: true)));
	}

	[Theory]
	[InlineData(RoundOutcome.Approved)]
	[InlineData(RoundOutcome.Skipped)]
	public void Next_AfterAnApprovedOrSkippedReview_OpensTheMergeOnTheSameAttempt(RoundOutcome outcome)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Merge, 3), PipelineRules.Next(RoundKind.Review, outcome, Progress(attempt: 3)));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	public void Next_AfterAMerge_CompletesTheCardOnTheAttemptItFinishedOn(int attempt)
	{
		Assert.Equal(PipelineStep.Complete(attempt), PipelineRules.Next(RoundKind.Merge, RoundOutcome.Merged, Progress(attempt)));
	}

	[Theory]
	[InlineData(1, 3)]
	[InlineData(2, 3)]
	[InlineData(3, 3)]
	[InlineData(1, 1)]
	public void Next_AfterAStaleBase_SendsTheCardBackThroughTheGatesOnTheSameAttemptEvenOnTheLastOne(int attempt, int limit)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Gate, attempt), PipelineRules.Next(RoundKind.Merge, RoundOutcome.Outdated, Progress(attempt, limit)));
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted)]
	public void Next_AfterASendBackWithoutAMergeAgent_OpensAnotherWorkRoundOnTheNextAttempt(RoundKind kind, RoundOutcome outcome)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Work, 2), PipelineRules.Next(kind, outcome, Progress(attempt: 1, limit: 3, hasMerger: false)));
		Assert.Equal(PipelineStep.Open(RoundKind.Work, 3), PipelineRules.Next(kind, outcome, Progress(attempt: 2, limit: 3, hasMerger: false)));
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	public void Next_AfterAFailedGateOrARejection_StillGoesBackToTheWorkerWhenTheProjectHasAMergeAgent(RoundKind kind, RoundOutcome outcome)
	{
		Assert.Equal(PipelineStep.Open(RoundKind.Work, 2), PipelineRules.Next(kind, outcome, Progress(attempt: 1, limit: 3, hasMerger: true)));
		Assert.Equal(PipelineStep.Stop(RoundKind.Work, 3), PipelineRules.Next(kind, outcome, Progress(attempt: 3, limit: 3, hasMerger: true)));
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundOutcome.Rejected)]
	[InlineData(RoundKind.Merge, RoundOutcome.Conflicted)]
	public void Next_AfterASendBackOnTheLastAttemptWithoutAMergeAgent_StopsTheCardAndResumesAtWork(RoundKind kind, RoundOutcome outcome)
	{
		Assert.Equal(PipelineStep.Stop(RoundKind.Work, 3), PipelineRules.Next(kind, outcome, Progress(attempt: 3, limit: 3, hasMerger: false)));
		Assert.Equal(PipelineStep.Stop(RoundKind.Work, 1), PipelineRules.Next(kind, outcome, Progress(attempt: 1, limit: 1, hasMerger: false)));
	}

	[Fact]
	public void Next_AfterASetbackBeyondTheLimit_StillStops()
	{
		Assert.Equal(PipelineStep.Stop(RoundKind.Work, 4), PipelineRules.Next(RoundKind.Gate, RoundOutcome.Failed, Progress(attempt: 4, limit: 3)));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	[InlineData(RoundKind.Resolve)]
	public void Next_AfterAnError_OpensAnotherRoundOfTheSameKindOnTheNextAttempt(RoundKind kind)
	{
		Assert.Equal(PipelineStep.Open(kind, 2), PipelineRules.Next(kind, RoundOutcome.Errored, Progress(attempt: 1, limit: 3)));
		Assert.Equal(PipelineStep.Open(kind, 3), PipelineRules.Next(kind, RoundOutcome.Errored, Progress(attempt: 2, limit: 3)));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	[InlineData(RoundKind.Resolve)]
	public void Next_AfterAnErrorOnTheLastAttempt_StopsTheCardAndResumesAtTheSameStage(RoundKind kind)
	{
		Assert.Equal(PipelineStep.Stop(kind, 3), PipelineRules.Next(kind, RoundOutcome.Errored, Progress(attempt: 3, limit: 3)));
	}

	[Theory]
	[InlineData(RoundKind.Work, 1)]
	[InlineData(RoundKind.Gate, 1)]
	[InlineData(RoundKind.Review, 2)]
	[InlineData(RoundKind.Merge, 3)]
	[InlineData(RoundKind.Resolve, 2)]
	public void Next_AfterABlock_StopsTheCardAtOnceAndResumesAtTheSameStageWhateverTheAttempt(RoundKind kind, int attempt)
	{
		Assert.Equal(PipelineStep.Stop(kind, attempt), PipelineRules.Next(kind, RoundOutcome.Blocked, Progress(attempt, limit: 10)));
	}

	[Theory]
	[InlineData(RoundKind.Work, RoundOutcome.Failed)]
	[InlineData(RoundKind.Work, RoundOutcome.Passed)]
	[InlineData(RoundKind.Work, RoundOutcome.Skipped)]
	[InlineData(RoundKind.Gate, RoundOutcome.Completed)]
	[InlineData(RoundKind.Gate, RoundOutcome.Merged)]
	[InlineData(RoundKind.Review, RoundOutcome.Passed)]
	[InlineData(RoundKind.Review, RoundOutcome.Failed)]
	[InlineData(RoundKind.Merge, RoundOutcome.Skipped)]
	[InlineData(RoundKind.Merge, RoundOutcome.Approved)]
	[InlineData(RoundKind.Work, RoundOutcome.Resolved)]
	[InlineData(RoundKind.Gate, RoundOutcome.Resolved)]
	[InlineData(RoundKind.Review, RoundOutcome.Resolved)]
	[InlineData(RoundKind.Merge, RoundOutcome.Resolved)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Completed)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Failed)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Conflicted)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Skipped)]
	[InlineData(RoundKind.Resolve, RoundOutcome.Merged)]
	public void Next_OfAnOutcomeThatDoesNotFollowFromTheKind_Throws(RoundKind kind, RoundOutcome outcome)
	{
		Assert.Throws<ArgumentException>(() => PipelineRules.Next(kind, outcome, Progress()));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	[InlineData(RoundKind.Resolve)]
	public void Next_OfAnInterruptedRound_Throws_BecauseAnInterruptedRoundIsResumedNotAdvanced(RoundKind kind)
	{
		Assert.Throws<ArgumentException>(() => PipelineRules.Next(kind, RoundOutcome.Interrupted, Progress()));
	}

	[Theory]
	[InlineData(RoundOutcome.Errored)]
	[InlineData(RoundOutcome.Blocked)]
	[InlineData(RoundOutcome.Completed)]
	public void Next_OfAnUndefinedKind_Throws(RoundOutcome outcome)
	{
		Assert.Throws<ArgumentException>(() => PipelineRules.Next((RoundKind)99, outcome, Progress()));
	}

	[Fact]
	public void Next_IsDefinedForEveryPairOfTheTableAndNoOther()
	{
		List<(RoundKind Kind, RoundOutcome Outcome)> defined = [];
		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			foreach (RoundOutcome outcome in Enum.GetValues<RoundOutcome>())
			{
				try
				{
					PipelineRules.Next(kind, outcome, Progress());
					defined.Add((kind, outcome));
				}
				catch (ArgumentException)
				{
					// Not a pair the state machine knows.
				}
			}
		}

		Assert.Equal(
			[
				(RoundKind.Work, RoundOutcome.Completed),
				(RoundKind.Work, RoundOutcome.Errored),
				(RoundKind.Work, RoundOutcome.Blocked),
				(RoundKind.Gate, RoundOutcome.Passed),
				(RoundKind.Gate, RoundOutcome.Failed),
				(RoundKind.Gate, RoundOutcome.Skipped),
				(RoundKind.Gate, RoundOutcome.Errored),
				(RoundKind.Gate, RoundOutcome.Blocked),
				(RoundKind.Review, RoundOutcome.Approved),
				(RoundKind.Review, RoundOutcome.Rejected),
				(RoundKind.Review, RoundOutcome.Skipped),
				(RoundKind.Review, RoundOutcome.Errored),
				(RoundKind.Review, RoundOutcome.Blocked),
				(RoundKind.Merge, RoundOutcome.Merged),
				(RoundKind.Merge, RoundOutcome.Conflicted),
				(RoundKind.Merge, RoundOutcome.Outdated),
				(RoundKind.Merge, RoundOutcome.Errored),
				(RoundKind.Merge, RoundOutcome.Blocked),
				(RoundKind.Resolve, RoundOutcome.Resolved),
				(RoundKind.Resolve, RoundOutcome.Errored),
				(RoundKind.Resolve, RoundOutcome.Blocked),
			],
			defined);
	}

	[Fact]
	public void Next_AcceptsEveryOutcomeThatOutcomeOfCanProduce()
	{
		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			foreach (RoundResult result in Enum.GetValues<RoundResult>())
			{
				if (PipelineRules.OutcomeOf(kind, result) is RoundOutcome outcome)
				{
					PipelineStep step = PipelineRules.Next(kind, outcome, Progress());

					Assert.True(Enum.IsDefined(step.Kind), $"{kind} {outcome} led nowhere.");
				}
			}
		}
	}

	[Fact]
	public void PipelineStep_Factories_RecordWhatTheStepDoes()
	{
		Assert.Equal(new PipelineStep(PipelineStepKind.Open, RoundKind.Review, 2), PipelineStep.Open(RoundKind.Review, 2));
		Assert.Equal(new PipelineStep(PipelineStepKind.Complete, null, 3), PipelineStep.Complete(3));
		Assert.Equal(new PipelineStep(PipelineStepKind.Stop, RoundKind.Work, 3), PipelineStep.Stop(RoundKind.Work, 3));
	}
}
