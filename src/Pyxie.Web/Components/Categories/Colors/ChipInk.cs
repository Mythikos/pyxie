using Pyxie.Data.Colors;

namespace Pyxie.Web.Components.Categories.Colors;

/// <summary>Derives the text color of a chip from its background, the way the design pairs its tints with their text: the background's own hue, deep on a light background and pale on a dark one, at 7:1. A background where neither black nor white reaches 7:1 gets whichever of the two reads better, which is never under 4.58:1, so every chip reads at the 4.5:1 the design asks for.</summary>
public static class ChipInk
{
	// Where the design's own pairs sit: Neutral 7.30, Green 6.86, and Pink 6.82 to 1.
	private const double PreferredContrast = 7.0;

	// The design's inks, --pyx-text-2, --pyx-primary-hover, and --pyx-pink-text, are at OKLCH lightness 0.417 to 0.450, and its tints at 0.945 to 0.953. A deep ink starts from the ink's lightness and a pale one from the tint's, and each moves away from the background until it reads at 7:1.
	private const double DeepStartLightness = 0.44;
	private const double PaleStartLightness = 0.95;
	private const double LightnessStep = 0.005;

	// The design's green and pink inks hold about 0.66 and 0.71 of the chroma sRGB allows at their lightness and hue. A pale ink stays nearly white, as the tints do.
	private const double DeepChromaShare = 0.7;
	private const double PaleChromaLimit = 0.03;

	// A background this gray or grayer gets gray text, as the neutral chip has; one this colorful or more gets the full hue. The neutral tint's chroma is 0.005 and the green and pink tints' are 0.024 and 0.020.
	private const double GrayChroma = 0.008;
	private const double HuedChroma = 0.02;

	private static readonly HexColor s_black = new(0x00, 0x00, 0x00);
	private static readonly HexColor s_white = new(0xFF, 0xFF, 0xFF);

	/// <summary>Gets the text color for a chip background.</summary>
	/// <param name="background">The chip's background.</param>
	/// <returns>A color of the background's hue that reads at 7:1 or more on it where black or white would, and otherwise the better of black and white.</returns>
	public static HexColor For(HexColor background)
	{
		bool deep = ContrastRatio.Between(s_black, background) >= ContrastRatio.Between(s_white, background);
		HexColor pole = deep ? s_black : s_white;
		if (ContrastRatio.Between(pole, background) < PreferredContrast)
		{
			return pole;
		}

		OklchColor color = OklchColor.From(background);
		double hued = SmoothStep(GrayChroma, HuedChroma, color.Chroma);

		// The scan's last step would be the pole itself, at lightness 0 or 1 where sRGB holds no chroma, and the pole reads at 7:1 here, so the scan always ends with an ink.
		int steps = (int)Math.Round((deep ? DeepStartLightness : 1 - PaleStartLightness) / LightnessStep);
		for (int step = 0; step < steps; step++)
		{
			double lightness = deep ? DeepStartLightness - (step * LightnessStep) : PaleStartLightness + (step * LightnessStep);
			double maxChroma = OklchColor.MaxChroma(lightness, color.Hue);
			double chroma = (deep ? DeepChromaShare * maxChroma : Math.Min(PaleChromaLimit, maxChroma)) * hued;
			HexColor ink = new OklchColor(lightness, chroma, color.Hue).ToHexColor();
			if (ContrastRatio.Between(ink, background) >= PreferredContrast)
			{
				return ink;
			}
		}

		return pole;
	}

	private static double SmoothStep(double low, double high, double value)
	{
		double position = Math.Clamp((value - low) / (high - low), 0, 1);

		return position * position * (3 - (2 * position));
	}
}
