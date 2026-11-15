using Pyxie.Web.Components.Repository.Display;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryDatesTests
{
	[Fact]
	public void Format_ShowsAUtcTimeInTheGivenTimeZone()
	{
		TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus two", "plus two");

		Assert.Equal("2026-11-13 12:21", RepositoryDates.Format(new DateTime(2026, 11, 13, 10, 21, 45, DateTimeKind.Utc), plusTwo));
	}

	[Fact]
	public void Format_CrossesMidnightWhenTheZoneIsAheadOfUtc()
	{
		TimeZoneInfo plusFive = TimeZoneInfo.CreateCustomTimeZone("plus-five", TimeSpan.FromHours(5), "plus five", "plus five");

		Assert.Equal("2027-01-01 02:30", RepositoryDates.Format(new DateTime(2026, 12, 31, 21, 30, 0, DateTimeKind.Utc), plusFive));
	}

	[Fact]
	public void Format_TreatsATimeOfUnspecifiedKindAsUtc()
	{
		TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus two", "plus two");

		Assert.Equal("2026-11-13 12:21", RepositoryDates.Format(new DateTime(2026, 11, 13, 10, 21, 0, DateTimeKind.Unspecified), plusTwo));
	}

	[Fact]
	public void Format_ShowsTheLocalTimeOfTheServerWhenNoZoneIsGiven()
	{
		DateTime utc = new(2026, 11, 13, 10, 21, 0, DateTimeKind.Utc);

		Assert.Equal(RepositoryDates.Format(utc, TimeZoneInfo.Local), RepositoryDates.Format(utc));
	}
}
