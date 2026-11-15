using Pyxie.Data.Enums;

using Xunit;

namespace Pyxie.Tests.Enums;

public sealed class CardLaneExtensionsTests
{
	[Theory]
	[InlineData(CardLane.Queue, "Queue")]
	[InlineData(CardLane.InProgress, "In Progress")]
	[InlineData(CardLane.Gates, "Gates")]
	[InlineData(CardLane.Review, "Review")]
	[InlineData(CardLane.Complete, "Complete")]
	[InlineData(CardLane.Closed, "Closed")]
	[InlineData((CardLane)99, "99")]
	public void DisplayName_ReturnsTheNamePeopleSee(CardLane lane, string expected)
	{
		Assert.Equal(expected, lane.DisplayName());
	}
}
