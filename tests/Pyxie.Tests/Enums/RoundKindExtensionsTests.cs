using Pyxie.Data.Enums;

using Xunit;

namespace Pyxie.Tests.Enums;

public sealed class RoundKindExtensionsTests
{
	[Theory]
	[InlineData(RoundKind.Work, "Work")]
	[InlineData(RoundKind.Gate, "Gates")]
	[InlineData(RoundKind.Review, "Review")]
	[InlineData(RoundKind.Merge, "Merge")]
	[InlineData(RoundKind.Resolve, "Resolve")]
	[InlineData((RoundKind)99, "99")]
	public void DisplayName_ReturnsTheNamePeopleSee(RoundKind kind, string expected)
	{
		Assert.Equal(expected, kind.DisplayName());
	}

	[Fact]
	public void DisplayName_NamesEveryDefinedKind_SoANewKindIsNotShownAsItsNumber()
	{
		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			Assert.False(char.IsDigit(kind.DisplayName()[0]), $"{kind} has no display name.");
		}
	}

	[Theory]
	[InlineData(RoundKind.Work, true)]
	[InlineData(RoundKind.Gate, false)]
	[InlineData(RoundKind.Review, true)]
	[InlineData(RoundKind.Merge, false)]
	[InlineData(RoundKind.Resolve, true)]
	[InlineData((RoundKind)99, false)]
	public void RunsAgent_IsTrueOnlyForWorkReviewAndResolveRounds(RoundKind kind, bool expected)
	{
		Assert.Equal(expected, kind.RunsAgent());
	}

	[Theory]
	[InlineData(RoundKind.Work, true)]
	[InlineData(RoundKind.Gate, false)]
	[InlineData(RoundKind.Review, false)]
	[InlineData(RoundKind.Merge, false)]
	[InlineData(RoundKind.Resolve, true)]
	[InlineData((RoundKind)99, false)]
	public void ChangesWork_IsTrueOnlyForTheRoundsWhereAnAgentWritesTheCardsCode(RoundKind kind, bool expected)
	{
		Assert.Equal(expected, kind.ChangesWork());
	}

	[Fact]
	public void ChangesWork_IsOnlyEverTrueForRoundsThatRunAnAgent()
	{
		Assert.All(Enum.GetValues<RoundKind>().Where(kind => kind.ChangesWork()), kind => Assert.True(kind.RunsAgent(), $"{kind} changes the work without running an agent."));
	}
}
