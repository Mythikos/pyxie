using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class AttemptProgressTests
{
	[Theory]
	[InlineData(2, 3, "2 of 3")]
	[InlineData(3, 3, "3 of 3")]
	[InlineData(1, 10, "1 of 10")]
	public void Text_SaysWhichAttemptTheCardIsOnOutOfTheLimit(int attempt, int attemptLimit, string expected)
	{
		Assert.Equal(expected, AttemptProgress.Text(attempt, attemptLimit));
	}

	[Fact]
	public void Text_GivesOnlyTheLimitBeforeTheFirstAttemptStarts()
	{
		Assert.Equal("3", AttemptProgress.Text(0, 3));
	}

	[Theory]
	[InlineData(2, 5, "2/5")]
	[InlineData(5, 5, "5/5")]
	[InlineData(1, 10, "1/10")]
	public void Compact_SaysWhichAttemptTheCardIsOnOverTheLimit(int attempt, int attemptLimit, string expected)
	{
		Assert.Equal(expected, AttemptProgress.Compact(attempt, attemptLimit));
	}

	[Fact]
	public void Compact_GivesOnlyTheLimitBeforeTheFirstAttemptStarts()
	{
		Assert.Equal("5", AttemptProgress.Compact(0, 5));
	}
}
