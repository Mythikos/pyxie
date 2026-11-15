using Pyxie.Data.Enums;
using Pyxie.Data.Services.Rules;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>The lanes rounds run in, and how a reported result becomes a round outcome (the first two columns of the state machine in docs/pipeline.md).</summary>
public sealed class PipelineRulesTests
{
	[Theory]
	[InlineData(RoundKind.Work, RoundResult.Succeeded, RoundOutcome.Completed)]
	[InlineData(RoundKind.Gate, RoundResult.Succeeded, RoundOutcome.Passed)]
	[InlineData(RoundKind.Gate, RoundResult.Failed, RoundOutcome.Failed)]
	[InlineData(RoundKind.Review, RoundResult.Succeeded, RoundOutcome.Approved)]
	[InlineData(RoundKind.Review, RoundResult.Failed, RoundOutcome.Rejected)]
	[InlineData(RoundKind.Merge, RoundResult.Succeeded, RoundOutcome.Merged)]
	[InlineData(RoundKind.Merge, RoundResult.Failed, RoundOutcome.Conflicted)]
	[InlineData(RoundKind.Merge, RoundResult.Outdated, RoundOutcome.Outdated)]
	[InlineData(RoundKind.Resolve, RoundResult.Succeeded, RoundOutcome.Resolved)]
	public void OutcomeOf_TurnsTheReportedResultIntoTheOutcomeOfThatKindOfRound(RoundKind kind, RoundResult result, RoundOutcome expected)
	{
		Assert.Equal(expected, PipelineRules.OutcomeOf(kind, result));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	[InlineData(RoundKind.Resolve)]
	public void OutcomeOf_ReadsAnErrorOrABlockForEveryKindOfRound(RoundKind kind)
	{
		Assert.Equal(RoundOutcome.Errored, PipelineRules.OutcomeOf(kind, RoundResult.Errored));
		Assert.Equal(RoundOutcome.Blocked, PipelineRules.OutcomeOf(kind, RoundResult.Blocked));
	}

	[Theory]
	[InlineData(RoundKind.Work, RoundResult.Failed)]
	[InlineData(RoundKind.Work, RoundResult.Outdated)]
	[InlineData(RoundKind.Gate, RoundResult.Outdated)]
	[InlineData(RoundKind.Review, RoundResult.Outdated)]
	[InlineData(RoundKind.Resolve, RoundResult.Failed)]
	[InlineData(RoundKind.Resolve, RoundResult.Outdated)]
	[InlineData(RoundKind.Work, (RoundResult)99)]
	[InlineData(RoundKind.Gate, (RoundResult)99)]
	[InlineData(RoundKind.Review, (RoundResult)99)]
	[InlineData(RoundKind.Merge, (RoundResult)99)]
	[InlineData(RoundKind.Resolve, (RoundResult)99)]
	[InlineData((RoundKind)99, RoundResult.Succeeded)]
	[InlineData((RoundKind)99, RoundResult.Failed)]
	[InlineData((RoundKind)99, RoundResult.Outdated)]
	[InlineData((RoundKind)99, RoundResult.Errored)]
	[InlineData((RoundKind)99, RoundResult.Blocked)]
	[InlineData((RoundKind)99, (RoundResult)99)]
	public void OutcomeOf_OfAResultThatIsNotValidForTheKind_IsNull(RoundKind kind, RoundResult result)
	{
		Assert.Null(PipelineRules.OutcomeOf(kind, result));
	}

	[Fact]
	public void OutcomeOf_IsDefinedForEveryPairOfTheTableAndNoOther()
	{
		List<(RoundKind Kind, RoundResult Result)> defined = [];
		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			foreach (RoundResult result in Enum.GetValues<RoundResult>())
			{
				if (PipelineRules.OutcomeOf(kind, result) is not null)
				{
					defined.Add((kind, result));
				}
			}
		}

		Assert.Equal(
			[
				(RoundKind.Work, RoundResult.Succeeded),
				(RoundKind.Work, RoundResult.Errored),
				(RoundKind.Work, RoundResult.Blocked),
				(RoundKind.Gate, RoundResult.Succeeded),
				(RoundKind.Gate, RoundResult.Failed),
				(RoundKind.Gate, RoundResult.Errored),
				(RoundKind.Gate, RoundResult.Blocked),
				(RoundKind.Review, RoundResult.Succeeded),
				(RoundKind.Review, RoundResult.Failed),
				(RoundKind.Review, RoundResult.Errored),
				(RoundKind.Review, RoundResult.Blocked),
				(RoundKind.Merge, RoundResult.Succeeded),
				(RoundKind.Merge, RoundResult.Failed),
				(RoundKind.Merge, RoundResult.Outdated),
				(RoundKind.Merge, RoundResult.Errored),
				(RoundKind.Merge, RoundResult.Blocked),
				(RoundKind.Resolve, RoundResult.Succeeded),
				(RoundKind.Resolve, RoundResult.Errored),
				(RoundKind.Resolve, RoundResult.Blocked),
			],
			defined);
	}

	[Theory]
	[InlineData(RoundOutcome.Completed, false)]
	[InlineData(RoundOutcome.Passed, false)]
	[InlineData(RoundOutcome.Failed, true)]
	[InlineData(RoundOutcome.Approved, false)]
	[InlineData(RoundOutcome.Rejected, true)]
	[InlineData(RoundOutcome.Merged, false)]
	[InlineData(RoundOutcome.Conflicted, true)]
	[InlineData(RoundOutcome.Outdated, false)]
	[InlineData(RoundOutcome.Resolved, false)]
	[InlineData(RoundOutcome.Skipped, false)]
	[InlineData(RoundOutcome.Errored, true)]
	[InlineData(RoundOutcome.Blocked, true)]
	[InlineData(RoundOutcome.Interrupted, false)]
	[InlineData((RoundOutcome)99, false)]
	public void RequiresNote_IsTrueForFailedRejectedConflictedErroredAndBlockedRounds(RoundOutcome outcome, bool expected)
	{
		Assert.Equal(expected, PipelineRules.RequiresNote(outcome));
	}

	[Theory]
	[InlineData(RoundKind.Work, CardLane.InProgress)]
	[InlineData(RoundKind.Gate, CardLane.Gates)]
	[InlineData(RoundKind.Review, CardLane.Review)]
	[InlineData(RoundKind.Merge, CardLane.Review)]
	[InlineData(RoundKind.Resolve, CardLane.InProgress)]
	public void LaneOf_NamesTheLaneARoundRunsIn_TheMergeRunsInReviewWithTheReviewAndAResolutionInProgressWithTheWork(RoundKind kind, CardLane lane)
	{
		Assert.Equal(lane, PipelineRules.LaneOf(kind));
	}

	[Fact]
	public void LaneOf_OfAnUndefinedKind_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => PipelineRules.LaneOf((RoundKind)99));
	}

	[Fact]
	public void LaneOf_NamesAnActiveLaneForEveryKind()
	{
		Assert.All(Enum.GetValues<RoundKind>(), kind => Assert.True(PipelineRules.IsActive(PipelineRules.LaneOf(kind)), $"{kind} runs in a lane the pipeline does not own."));
	}

	[Theory]
	[InlineData(CardLane.Queue, false)]
	[InlineData(CardLane.InProgress, true)]
	[InlineData(CardLane.Gates, true)]
	[InlineData(CardLane.Review, true)]
	[InlineData(CardLane.Complete, false)]
	[InlineData(CardLane.Closed, false)]
	[InlineData((CardLane)99, false)]
	public void IsActive_IsTrueWhereThePipelineOwnsTheCard(CardLane lane, bool expected)
	{
		Assert.Equal(expected, PipelineRules.IsActive(lane));
	}

	[Fact]
	public void ActiveLanes_AreInPipelineOrderAgreeWithIsActiveAndAreAReadOnlyView()
	{
		Assert.Equal([CardLane.InProgress, CardLane.Gates, CardLane.Review], PipelineRules.ActiveLanes);
		Assert.Equal(Enum.GetValues<CardLane>().Where(PipelineRules.IsActive), PipelineRules.ActiveLanes);
		Assert.True(((ICollection<CardLane>)PipelineRules.ActiveLanes).IsReadOnly, "Callers must not be able to change the lanes the pipeline owns.");
	}
}
