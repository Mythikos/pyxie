using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class ElapsedTimeTests
{
	[Theory]
	[InlineData(0, 0, "now")]
	[InlineData(0, 59, "now")]
	[InlineData(1, 0, "1m")]
	[InlineData(8, 30, "8m")]
	[InlineData(59, 59, "59m")]
	public void Describe_SaysNowUnderAMinuteThenMinutes(int minutes, int seconds, string expected)
	{
		Assert.Equal(expected, ElapsedTime.Describe(new TimeSpan(0, minutes, seconds)));
	}

	[Theory]
	[InlineData(1, 5, "1h 5m")]
	[InlineData(1, 0, "1h")]
	[InlineData(3, 59, "3h 59m")]
	[InlineData(23, 0, "23h")]
	public void Describe_SaysHoursWithTheMinutesLeftOverAndLeavesOutNone(int hours, int minutes, string expected)
	{
		Assert.Equal(expected, ElapsedTime.Describe(new TimeSpan(hours, minutes, 0)));
	}

	[Theory]
	[InlineData(1, 0, "1d")]
	[InlineData(2, 3, "2d 3h")]
	[InlineData(10, 23, "10d 23h")]
	public void Describe_SaysDaysWithTheHoursLeftOverFromADay(int days, int hours, string expected)
	{
		Assert.Equal(expected, ElapsedTime.Describe(new TimeSpan(days, hours, 40, 0)));
	}

	[Fact]
	public void Describe_CountsATimeThatSteppedBackAsNone()
	{
		Assert.Equal("now", ElapsedTime.Describe(TimeSpan.FromMinutes(-5)));
	}
}
