using System.Globalization;

namespace Pyxie.Web.Components.Repository.Display;

/// <summary>Writes the times git reports, which are UTC, in the reader's time zone.</summary>
public static class RepositoryDates
{
	private const string Pattern = "yyyy-MM-dd HH:mm";

	/// <summary>Formats a UTC time in the local time zone of the server, which is the one the person running Pyxie lives in.</summary>
	/// <param name="utc">The time in UTC.</param>
	/// <returns>Text such as <c>2026-11-13 10:21</c>.</returns>
	public static string Format(DateTime utc)
	{
		return Format(utc, TimeZoneInfo.Local);
	}

	/// <summary>Formats a UTC time in a time zone.</summary>
	/// <param name="utc">The time in UTC.</param>
	/// <param name="zone">The time zone to show it in.</param>
	/// <returns>Text such as <c>2026-11-13 12:21</c>.</returns>
	public static string Format(DateTime utc, TimeZoneInfo zone)
	{
		return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).ToString(Pattern, CultureInfo.InvariantCulture);
	}
}
