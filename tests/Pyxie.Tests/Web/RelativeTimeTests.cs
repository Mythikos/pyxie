using Pyxie.Web.Components.Display;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RelativeTimeTests
{
	private static readonly DateTime s_now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

	[Theory]
	[InlineData(0, "just now")]
	[InlineData(59, "just now")]
	[InlineData(60, "1 minute ago")]
	[InlineData(119, "1 minute ago")]
	[InlineData(120, "2 minutes ago")]
	[InlineData(3599, "59 minutes ago")]
	[InlineData(3600, "1 hour ago")]
	[InlineData(7200, "2 hours ago")]
	public void Describe_CountsMinutesAndHoursWithinTheSameDay(int secondsAgo, string expected)
	{
		Assert.Equal(expected, RelativeTime.Describe(s_now.AddSeconds(-secondsAgo), s_now, TimeZoneInfo.Utc));
	}

	[Theory]
	[InlineData(1, "yesterday")]
	[InlineData(2, "2 days ago")]
	[InlineData(6, "6 days ago")]
	[InlineData(7, "Nov 7")]
	[InlineData(30, "Oct 15")]
	public void Describe_CountsCalendarDaysAndThenShowsTheDate(int daysAgo, string expected)
	{
		Assert.Equal(expected, RelativeTime.Describe(s_now.AddDays(-daysAgo), s_now, TimeZoneInfo.Utc));
	}

	[Fact]
	public void Describe_AddsTheYearForAnEarlierYear()
	{
		Assert.Equal("Nov 2, 2025", RelativeTime.Describe(new DateTime(2025, 11, 2, 9, 0, 0, DateTimeKind.Utc), s_now, TimeZoneInfo.Utc));
	}

	[Fact]
	public void Describe_DecidesYesterdayInTheReadersTimeZone()
	{
		TimeZoneInfo plusTen = TimeZoneInfo.CreateCustomTimeZone("plus-ten", TimeSpan.FromHours(10), "plus ten", "plus ten");
		DateTime now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);
		DateTime eightHoursEarlier = now.AddHours(-8);

		Assert.Equal("8 hours ago", RelativeTime.Describe(eightHoursEarlier, now, TimeZoneInfo.Utc));
		Assert.Equal("yesterday", RelativeTime.Describe(eightHoursEarlier, now, plusTen));
	}

	[Fact]
	public void Describe_KeepsCountingHoursForARecentTimeJustBeforeLocalMidnight()
	{
		DateTime now = new(2026, 11, 14, 0, 30, 0, DateTimeKind.Utc);

		Assert.Equal("1 hour ago", RelativeTime.Describe(now.AddHours(-1), now, TimeZoneInfo.Utc));
	}

	[Fact]
	public void Describe_TreatsATimeOfUnspecifiedKindAsUtc()
	{
		Assert.Equal("2 hours ago", RelativeTime.Describe(DateTime.SpecifyKind(s_now.AddHours(-2), DateTimeKind.Unspecified), s_now, TimeZoneInfo.Utc));
	}
}
