using Pyxie.Data.Colors;

namespace Pyxie.Web.Components.Categories.Colors;

/// <summary>Owns how well one color reads against another: the WCAG 2 contrast ratio, from 1:1 for a color on itself to 21:1 for black on white. The design asks for 4.5:1 or more for text.</summary>
public static class ContrastRatio
{
	/// <summary>The ratio the design asks for, at least, between text and what it is on.</summary>
	public const double MinimumForText = 4.5;

	/// <summary>Gets the contrast ratio between two colors, whichever is lighter.</summary>
	/// <param name="first">One color.</param>
	/// <param name="second">The other color.</param>
	/// <returns>The ratio, from 1 to 21.</returns>
	public static double Between(HexColor first, HexColor second)
	{
		double firstLuminance = RelativeLuminance(first);
		double secondLuminance = RelativeLuminance(second);

		return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
	}

	/// <summary>Gets the relative luminance WCAG 2 defines for a color.</summary>
	/// <param name="color">The color.</param>
	/// <returns>The luminance, from 0 for black to 1 for white.</returns>
	public static double RelativeLuminance(HexColor color)
	{
		return (0.2126 * ToLinear(color.Red)) + (0.7152 * ToLinear(color.Green)) + (0.0722 * ToLinear(color.Blue));
	}

	/// <summary>Undoes the sRGB transfer curve on one 8-bit channel.</summary>
	/// <param name="channel">The channel as the screen gets it.</param>
	/// <returns>The channel's linear light, from 0 to 1.</returns>
	public static double ToLinear(byte channel)
	{
		double encoded = channel / 255.0;

		return encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
	}
}
