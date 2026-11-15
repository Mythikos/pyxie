using Pyxie.Data.Enums;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RoundStageTonesTests
{
	[Theory]
	[InlineData(RoundKind.Work, RoundStageTone.Work)]
	[InlineData(RoundKind.Review, RoundStageTone.Work)]
	[InlineData(RoundKind.Gate, RoundStageTone.Gate)]
	[InlineData(RoundKind.Resolve, RoundStageTone.Resolve)]
	[InlineData(RoundKind.Merge, RoundStageTone.Merge)]
	public void Of_ToneOfEachKindFollowsTheStageItRunsIn(RoundKind kind, RoundStageTone expected)
	{
		Assert.Equal(expected, RoundStageTones.Of(kind));
	}

	[Fact]
	public void Of_GivesEveryDefinedKindATone()
	{
		List<RoundKind> untoned = [];

		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			try
			{
				RoundStageTones.Of(kind);
			}
			catch (ArgumentOutOfRangeException)
			{
				untoned.Add(kind);
			}
		}

		Assert.True(untoned.Count == 0, $"These round kinds would show no color on a running card, add them to RoundStageTones: {string.Join(", ", untoned)}");
	}

	[Fact]
	public void Of_RefusesAKindOutsideTheEnumeration()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => RoundStageTones.Of((RoundKind)999));
	}
}
