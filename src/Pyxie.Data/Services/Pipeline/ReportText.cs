using System.Globalization;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>Shortens text that goes into rounds and briefings so it fits its limit, saying how much was cut.</summary>
public static class ReportText
{
	// Room for the "[truncated: N characters omitted]" marker, so a shortened value stays within its limit.
	private const int MarkerReserve = 64;

	/// <summary>Keeps the beginning and as much of the end as fits, where command output usually puts its summary, with a marker in the middle saying how much was cut.</summary>
	/// <param name="value">The text.</param>
	/// <param name="maxLength">The longest the result may be, marker included. Must be larger than <paramref name="headLength"/> plus 64.</param>
	/// <param name="headLength">How much of the beginning to keep.</param>
	/// <returns><paramref name="value"/> when it fits; otherwise its head, the marker, and its tail.</returns>
	public static string Excerpt(string value, int maxLength, int headLength)
	{
		if (value.Length <= maxLength)
		{
			return value;
		}

		int tailLength = Math.Max(0, maxLength - headLength - MarkerReserve);
		string marker = string.Create(CultureInfo.InvariantCulture, $"\n[truncated: {value.Length - headLength - tailLength} characters omitted]\n");

		return string.Concat(value.AsSpan(0, headLength), marker, value.AsSpan(value.Length - tailLength));
	}

	/// <summary>Keeps the beginning, with a marker at the end saying how much was cut.</summary>
	/// <param name="value">The text.</param>
	/// <param name="maxLength">The longest the result may be, marker included. Must be larger than 64.</param>
	/// <returns><paramref name="value"/> when it fits; otherwise its head and the marker.</returns>
	public static string Truncate(string value, int maxLength)
	{
		if (value.Length <= maxLength)
		{
			return value;
		}

		int kept = Math.Max(0, maxLength - MarkerReserve);
		string marker = string.Create(CultureInfo.InvariantCulture, $"\n[truncated: {value.Length - kept} characters omitted]");

		return string.Concat(value.AsSpan(0, kept), marker);
	}

	/// <summary>Shortens text to one line's worth, ending in an ellipsis when it was cut. Suited to notes, which have no room for a marker.</summary>
	/// <param name="value">The text.</param>
	/// <param name="maxLength">The longest the result may be, ellipsis included.</param>
	/// <returns><paramref name="value"/> when it fits; otherwise its first <paramref name="maxLength"/> - 1 characters and an ellipsis.</returns>
	public static string Clip(string value, int maxLength)
	{
		return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
	}
}
