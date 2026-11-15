using Pyxie.Data.Enums;

using Xunit;

namespace Pyxie.Tests.Enums;

public sealed class CardStateExtensionsTests
{
	[Theory]
	[InlineData(CardState.Ready, "Ready")]
	[InlineData(CardState.Error, "Error")]
	[InlineData((CardState)99, "99")]
	public void DisplayName_ReturnsTheNamePeopleSee(CardState state, string expected)
	{
		Assert.Equal(expected, state.DisplayName());
	}

	[Fact]
	public void DisplayName_NamesEveryDefinedState_SoANewStateIsNotShownAsItsNumber()
	{
		foreach (CardState state in Enum.GetValues<CardState>())
		{
			Assert.False(char.IsDigit(state.DisplayName()[0]), $"{state} has no display name.");
		}
	}
}
