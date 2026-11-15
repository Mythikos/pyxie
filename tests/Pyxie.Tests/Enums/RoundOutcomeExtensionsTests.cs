using Pyxie.Data.Enums;

using Xunit;

namespace Pyxie.Tests.Enums;

public sealed class RoundOutcomeExtensionsTests
{
	[Theory]
	[InlineData(RoundOutcome.Completed, "Completed")]
	[InlineData(RoundOutcome.Passed, "Passed")]
	[InlineData(RoundOutcome.Failed, "Failed")]
	[InlineData(RoundOutcome.Approved, "Approved")]
	[InlineData(RoundOutcome.Rejected, "Rejected")]
	[InlineData(RoundOutcome.Merged, "Merged")]
	[InlineData(RoundOutcome.Conflicted, "Conflicted")]
	[InlineData(RoundOutcome.Outdated, "Outdated")]
	[InlineData(RoundOutcome.Resolved, "Resolved")]
	[InlineData(RoundOutcome.Skipped, "Skipped")]
	[InlineData(RoundOutcome.Errored, "Errored")]
	[InlineData(RoundOutcome.Blocked, "Blocked")]
	[InlineData(RoundOutcome.Interrupted, "Interrupted")]
	[InlineData((RoundOutcome)99, "99")]
	public void DisplayName_ReturnsTheNamePeopleSee(RoundOutcome outcome, string expected)
	{
		Assert.Equal(expected, ((RoundOutcome?)outcome).DisplayName());
	}

	[Fact]
	public void DisplayName_OfARoundWithoutAnOutcome_IsRunning()
	{
		RoundOutcome? running = null;

		Assert.Equal("Running", running.DisplayName());
	}

	[Fact]
	public void DisplayName_NamesEveryDefinedOutcome_SoANewOutcomeIsNotShownAsItsNumber()
	{
		foreach (RoundOutcome outcome in Enum.GetValues<RoundOutcome>())
		{
			Assert.False(char.IsDigit(((RoundOutcome?)outcome).DisplayName()[0]), $"{outcome} has no display name.");
		}
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
	[InlineData(RoundOutcome.Errored, false)]
	[InlineData(RoundOutcome.Blocked, false)]
	[InlineData(RoundOutcome.Interrupted, false)]
	[InlineData((RoundOutcome)99, false)]
	public void IsSendBack_IsTrueOnlyWhenTheGatesFailedTheReviewerRejectedOrTheMergeConflicted(RoundOutcome outcome, bool expected)
	{
		Assert.Equal(expected, outcome.IsSendBack());
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
	[InlineData(RoundOutcome.Blocked, false)]
	[InlineData(RoundOutcome.Interrupted, false)]
	[InlineData((RoundOutcome)99, false)]
	public void IsSetback_IsTrueForEverySendBackAndForAnErrorButNeverForOutdatedResolvedSkippedBlockedOrInterrupted(RoundOutcome outcome, bool expected)
	{
		Assert.Equal(expected, outcome.IsSetback());
	}
}
