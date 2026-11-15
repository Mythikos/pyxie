using Pyxie.Data.Enums;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RoundOutcomeTonesTests
{
	[Theory]
	[InlineData(RoundOutcome.Completed, RoundOutcomeTone.Success)]
	[InlineData(RoundOutcome.Passed, RoundOutcomeTone.Success)]
	[InlineData(RoundOutcome.Approved, RoundOutcomeTone.Success)]
	[InlineData(RoundOutcome.Resolved, RoundOutcomeTone.Success)]
	[InlineData(RoundOutcome.Merged, RoundOutcomeTone.Success)]
	[InlineData(RoundOutcome.Failed, RoundOutcomeTone.Danger)]
	[InlineData(RoundOutcome.Rejected, RoundOutcomeTone.Danger)]
	[InlineData(RoundOutcome.Conflicted, RoundOutcomeTone.Danger)]
	[InlineData(RoundOutcome.Errored, RoundOutcomeTone.Danger)]
	[InlineData(RoundOutcome.Blocked, RoundOutcomeTone.Danger)]
	[InlineData(RoundOutcome.Outdated, RoundOutcomeTone.Warning)]
	[InlineData(RoundOutcome.Skipped, RoundOutcomeTone.Muted)]
	[InlineData(RoundOutcome.Interrupted, RoundOutcomeTone.Muted)]
	public void Of_ToneOfEachOutcomeFollowsWhatItMeans(RoundOutcome outcome, RoundOutcomeTone expected)
	{
		Assert.Equal(expected, RoundOutcomeTones.Of(outcome));
	}

	[Fact]
	public void Of_ARunningRoundHasNoTone()
	{
		Assert.Equal(RoundOutcomeTone.None, RoundOutcomeTones.Of(null));
	}

	[Fact]
	public void Of_GivesEveryDefinedOutcomeATone()
	{
		List<RoundOutcome> untoned = [.. Enum.GetValues<RoundOutcome>().Where(outcome => RoundOutcomeTones.Of(outcome) == RoundOutcomeTone.None)];

		Assert.True(untoned.Count == 0, $"These outcomes would show no tone in history, add them to RoundOutcomeTones: {string.Join(", ", untoned)}");
	}

	[Fact]
	public void Of_ToneOfAnOutcomeOutsideTheEnumerationIsNone()
	{
		Assert.Equal(RoundOutcomeTone.None, RoundOutcomeTones.Of((RoundOutcome)999));
	}
}
