using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Stages that do not apply to a card are skipped, and a card whose latest work was approved goes from the gates straight to the merge.</summary>
public sealed class PipelineRulesSkipTests
{
	private static CardProgress Progress(bool hasGates, bool hasReviewer)
	{
		return new CardProgress(Attempt: 1, AttemptLimit: 3, hasGates, hasReviewer, HasMerger: false, ApprovedSinceLastWork: false);
	}

	[Fact]
	public void SkipReason_OfTheGatesWithoutAnEnabledGate_IsTheNoGatesNote()
	{
		Assert.Equal("No gates are enabled.", PipelineRules.NoGatesNote);
		Assert.Equal(PipelineRules.NoGatesNote, PipelineRules.SkipReason(RoundKind.Gate, Progress(hasGates: false, hasReviewer: true)));
	}

	[Fact]
	public void SkipReason_OfTheReviewWithoutAReviewer_IsTheNoReviewerNote()
	{
		Assert.Equal("No reviewer is assigned.", PipelineRules.NoReviewerNote);
		Assert.Equal(PipelineRules.NoReviewerNote, PipelineRules.SkipReason(RoundKind.Review, Progress(hasGates: true, hasReviewer: false)));
	}

	[Fact]
	public void SkipReason_OfAStageThatApplies_IsNull()
	{
		Assert.Null(PipelineRules.SkipReason(RoundKind.Gate, Progress(hasGates: true, hasReviewer: false)));
		Assert.Null(PipelineRules.SkipReason(RoundKind.Review, Progress(hasGates: false, hasReviewer: true)));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Merge)]
	public void SkipReason_OfWorkAndMerge_IsNullWhateverTheCardHas(RoundKind kind)
	{
		Assert.Null(PipelineRules.SkipReason(kind, Progress(hasGates: false, hasReviewer: false)));
	}

	[Fact]
	public void IsApprovedSinceLastWork_OfNoRounds_IsFalse()
	{
		Assert.False(PipelineRules.IsApprovedSinceLastWork([]));
	}

	[Fact]
	public void IsApprovedSinceLastWork_OfWorkThatWasOnlyGated_IsFalse()
	{
		CardRound[] rounds = [TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed)];

		Assert.False(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Theory]
	[InlineData(RoundOutcome.Approved)]
	[InlineData(RoundOutcome.Skipped)]
	public void IsApprovedSinceLastWork_OfWorkWhoseReviewApprovedOrWasSkipped_IsTrue(RoundOutcome review)
	{
		CardRound[] rounds = [TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed), TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed), TestRound.Create(3, RoundKind.Review, review)];

		Assert.True(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Theory]
	[InlineData(RoundOutcome.Rejected)]
	[InlineData(RoundOutcome.Errored)]
	[InlineData(RoundOutcome.Interrupted)]
	[InlineData(RoundOutcome.Blocked)]
	public void IsApprovedSinceLastWork_OfAReviewThatDidNotApprove_IsFalse(RoundOutcome review)
	{
		CardRound[] rounds = [TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed), TestRound.Create(2, RoundKind.Review, review)];

		Assert.False(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Fact]
	public void IsApprovedSinceLastWork_StaysTrueThroughAnOutdatedMergeAndTheGatesThatCheckItAgain()
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Outdated),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed),
		];

		Assert.True(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Theory]
	[InlineData(RoundOutcome.Conflicted)]
	[InlineData(RoundOutcome.Errored)]
	public void IsApprovedSinceLastWork_IsFalseOnceTheWorkIsDoneAgain(RoundOutcome merge)
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(3, RoundKind.Merge, merge),
			TestRound.Create(4, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(5, RoundKind.Gate, RoundOutcome.Passed),
		];

		Assert.False(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Fact]
	public void IsApprovedSinceLastWork_OfAnApprovalAfterARejectionAndNewWork_IsTrue()
	{
		CardRound[] rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Rejected),
			TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(4, RoundKind.Review, RoundOutcome.Approved),
		];

		Assert.True(PipelineRules.IsApprovedSinceLastWork(rounds));
	}

	[Fact]
	public void IsApprovedSinceLastWork_ReadsTheRoundsInSequenceOrderWhateverOrderTheyArriveIn()
	{
		CardRound[] approvedThenReworked =
		[
			TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Review, RoundOutcome.Approved),
		];
		CardRound[] reworkedThenApproved =
		[
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved),
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed),
			TestRound.Create(2, RoundKind.Work, RoundOutcome.Completed),
		];

		Assert.False(PipelineRules.IsApprovedSinceLastWork(approvedThenReworked));
		Assert.True(PipelineRules.IsApprovedSinceLastWork(reworkedThenApproved));
	}
}
