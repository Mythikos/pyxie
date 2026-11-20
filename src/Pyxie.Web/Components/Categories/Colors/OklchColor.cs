using Pyxie.Data.Colors;

namespace Pyxie.Web.Components.Categories.Colors;

/// <summary>A color in OKLCH, the polar form of Björn Ottosson's OKLab, where lightness and chroma can change while the hue a person sees stays the same. This is how a chip's text keeps the hue of its background.</summary>
/// <param name="Lightness">The perceived lightness, from 0 for black to 1 for white.</param>
/// <param name="Chroma">How colorful the color is, from 0 for a gray; sRGB holds up to about 0.32.</param>
/// <param name="Hue">The hue angle, in radians.</param>
public readonly record struct OklchColor(double Lightness, double Chroma, double Hue)
{
	// The widest chroma searched for: more than any sRGB color has.
	private const double ChromaCeiling = 0.4;
	private const int ChromaSearchSteps = 20;

	// How far outside 0..1 a linear channel may fall from rounding and still count as inside sRGB.
	private const double GamutTolerance = 1e-9;

	/// <summary>Converts an sRGB color to OKLCH.</summary>
	/// <param name="color">The color.</param>
	/// <returns>The same color in OKLCH.</returns>
	public static OklchColor From(HexColor color)
	{
		double red = ContrastRatio.ToLinear(color.Red);
		double green = ContrastRatio.ToLinear(color.Green);
		double blue = ContrastRatio.ToLinear(color.Blue);

		double longCone = Math.Cbrt((0.4122214708 * red) + (0.5363325363 * green) + (0.0514459929 * blue));
		double mediumCone = Math.Cbrt((0.2119034982 * red) + (0.6806995451 * green) + (0.1073969566 * blue));
		double shortCone = Math.Cbrt((0.0883024619 * red) + (0.2817188376 * green) + (0.6299787005 * blue));

		double lightness = (0.2104542553 * longCone) + (0.7936177850 * mediumCone) - (0.0040720468 * shortCone);
		double greenRed = (1.9779984951 * longCone) - (2.4285922050 * mediumCone) + (0.4505937099 * shortCone);
		double blueYellow = (0.0259040371 * longCone) + (0.7827717662 * mediumCone) - (0.8086757660 * shortCone);

		return new OklchColor(lightness, Math.Sqrt((greenRed * greenRed) + (blueYellow * blueYellow)), Math.Atan2(blueYellow, greenRed));
	}

	/// <summary>Gets the most chroma an sRGB color can have at a lightness and hue.</summary>
	/// <param name="lightness">The lightness, from 0 to 1.</param>
	/// <param name="hue">The hue angle, in radians.</param>
	/// <returns>The chroma at the edge of sRGB, within about 4e-7.</returns>
	public static double MaxChroma(double lightness, double hue)
	{
		double inside = 0;
		double outside = ChromaCeiling;
		for (int step = 0; step < ChromaSearchSteps; step++)
		{
			double middle = (inside + outside) / 2;
			if (new OklchColor(lightness, middle, hue).IsInGamut())
			{
				inside = middle;
			}
			else
			{
				outside = middle;
			}
		}

		return inside;
	}

	/// <summary>Gets the nearest color the screen can show: each channel kept within sRGB and rounded to eight bits.</summary>
	/// <returns>The color.</returns>
	public HexColor ToHexColor()
	{
		(double red, double green, double blue) = ToLinearSrgb();

		return new HexColor(ToChannel(red), ToChannel(green), ToChannel(blue));
	}

	/// <summary>Determines whether sRGB holds this color.</summary>
	/// <returns><see langword="true"/> when every linear channel is within 0 to 1.</returns>
	public bool IsInGamut()
	{
		(double red, double green, double blue) = ToLinearSrgb();

		return IsInRange(red) && IsInRange(green) && IsInRange(blue);
	}

	private static bool IsInRange(double linear)
	{
		return linear >= -GamutTolerance && linear <= 1 + GamutTolerance;
	}

	private static byte ToChannel(double linear)
	{
		double clipped = Math.Clamp(linear, 0, 1);
		double encoded = clipped <= 0.0031308 ? 12.92 * clipped : (1.055 * Math.Pow(clipped, 1 / 2.4)) - 0.055;

		return (byte)Math.Round(Math.Clamp(encoded, 0, 1) * 255, MidpointRounding.AwayFromZero);
	}

	private (double Red, double Green, double Blue) ToLinearSrgb()
	{
		double greenRed = Chroma * Math.Cos(Hue);
		double blueYellow = Chroma * Math.Sin(Hue);

		double longCone = Math.Pow(Lightness + (0.3963377774 * greenRed) + (0.2158037573 * blueYellow), 3);
		double mediumCone = Math.Pow(Lightness - (0.1055613458 * greenRed) - (0.0638541728 * blueYellow), 3);
		double shortCone = Math.Pow(Lightness - (0.0894841775 * greenRed) - (1.2914855480 * blueYellow), 3);

		double red = (4.0767416621 * longCone) - (3.3077115913 * mediumCone) + (0.2309699292 * shortCone);
		double green = (-1.2684380046 * longCone) + (2.6097574011 * mediumCone) - (0.3413193965 * shortCone);
		double blue = (-0.0041960863 * longCone) - (0.7034186147 * mediumCone) + (1.7076147010 * shortCone);

		return (red, green, blue);
	}
}
