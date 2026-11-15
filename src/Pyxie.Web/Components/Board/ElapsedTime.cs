using System.Globalization;

namespace Pyxie.Web.Components.Board;

/// <summary>Words how long a round has been running, in the short form on a board card: <c>now</c>, <c>8m</c>, <c>1h 5m</c>, <c>2d 3h</c>.</summary>
public static class ElapsedTime
{
	/// <summary>Gets how long something has been running.</summary>
	/// <param name="elapsed">The time since it started; a negative span, from a clock that stepped back, counts as none.</param>
	/// <returns><c>now</c> under a minute, then minutes, then hours with the minutes left over, and from a day days with the hours left over; a remainder of zero is left out, as in <c>1h</c> and <c>2d</c>.</returns>
	public static string Describe(TimeSpan elapsed)
	{
		if (elapsed < TimeSpan.FromMinutes(1))
		{
			return "now";
		}

		if (elapsed < TimeSpan.FromHours(1))
		{
			return string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m");
		}

		if (elapsed < TimeSpan.FromDays(1))
		{
			return Join((int)elapsed.TotalHours, "h", elapsed.Minutes, "m");
		}

		return Join((int)elapsed.TotalDays, "d", elapsed.Hours, "h");
	}

	private static string Join(int larger, string largerUnit, int smaller, string smallerUnit)
	{
		return smaller == 0 ? string.Create(CultureInfo.InvariantCulture, $"{larger}{largerUnit}") : string.Create(CultureInfo.InvariantCulture, $"{larger}{largerUnit} {smaller}{smallerUnit}");
	}
}
