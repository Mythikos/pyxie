using System.Globalization;

namespace Pyxie.Web.Components.Repository.Display;

/// <summary>Writes byte counts the way file listings do.</summary>
public static class FileSize
{
	private const double Step = 1024;
	private static readonly string[] s_units = ["B", "KB", "MB", "GB", "TB"];

	/// <summary>Formats a size in bytes with the largest unit that keeps it under 1024, one decimal place above bytes, in the invariant culture.</summary>
	/// <param name="bytes">The size in bytes; a negative size reads as 0.</param>
	/// <returns>Text such as <c>512 B</c>, <c>1.5 KB</c>, or <c>3.0 MB</c>.</returns>
	public static string Format(long bytes)
	{
		double value = Math.Max(0, bytes);
		int unit = 0;

		// Rounding to one decimal can reach 1024 (1023.97 KB), which must read as 1.0 MB.
		while (Math.Round(value, 1) >= Step && unit < s_units.Length - 1)
		{
			value /= Step;
			unit++;
		}

		return unit == 0 ? $"{value.ToString("0", CultureInfo.InvariantCulture)} {s_units[0]}" : $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {s_units[unit]}";
	}
}
