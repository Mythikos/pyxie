using System.Globalization;

namespace Pyxie.Web.Components.Display;

/// <summary>Says how long ago something happened, the way the board and the repository history show it: "5 minutes ago", "yesterday", "Nov 2".</summary>
public static class RelativeTime
{
	private const int DaysShownAsDays = 7;

	/// <summary>Describes a past time relative to now, in the reader's time zone.</summary>
	/// <param name="utc">When it happened, in UTC.</param>
	/// <param name="nowUtc">The current time, in UTC.</param>
	/// <param name="zone">The reader's time zone, which decides what "yesterday" is.</param>
	/// <returns>Text such as <c>just now</c>, <c>1 minute ago</c>, <c>3 hours ago</c>, <c>yesterday</c>, <c>4 days ago</c>, <c>Nov 2</c>, or <c>Nov 2, 2025</c>.</returns>
	public static string Describe(DateTime utc, DateTime nowUtc, TimeZoneInfo zone)
	{
		DateTime then = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
		DateTime now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
		TimeSpan elapsed = now - then;

		if (elapsed < TimeSpan.FromMinutes(1))
		{
			return "just now";
		}

		if (elapsed < TimeSpan.FromHours(1))
		{
			return Ago((int)elapsed.TotalMinutes, "minute");
		}

		DateTime localThen = TimeZoneInfo.ConvertTimeFromUtc(then, zone);
		DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
		int days = (localNow.Date - localThen.Date).Days;

		if (days == 0 || elapsed < TimeSpan.FromHours(6))
		{
			return Ago((int)elapsed.TotalHours, "hour");
		}

		if (days == 1)
		{
			return "yesterday";
		}

		if (days < DaysShownAsDays)
		{
			return Ago(days, "day");
		}

		string format = localThen.Year == localNow.Year ? "MMM d" : "MMM d, yyyy";

		return localThen.ToString(format, CultureInfo.InvariantCulture);
	}

	private static string Ago(int count, string unit)
	{
		return count == 1 ? $"1 {unit} ago" : string.Create(CultureInfo.InvariantCulture, $"{count} {unit}s ago");
	}
}
