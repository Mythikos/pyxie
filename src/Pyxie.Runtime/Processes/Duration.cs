using System.Globalization;

namespace Pyxie.Runtime.Processes;

/// <summary>Describes how long something ran or may run, for notes and reports.</summary>
public static class Duration
{
	/// <summary>Describes a duration in the largest unit that keeps it readable.</summary>
	/// <param name="duration">The duration.</param>
	/// <returns>For example <c>1 minute</c>, <c>90 minutes</c>, <c>2m 5s</c>, or <c>3.4s</c>.</returns>
	public static string Describe(TimeSpan duration)
	{
		if (duration.TotalMinutes >= 1 && duration.Seconds == 0 && duration.Milliseconds == 0)
		{
			int minutes = (int)duration.TotalMinutes;

			return minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
		}

		return duration.TotalMinutes >= 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds}s") : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0}s");
	}
}
