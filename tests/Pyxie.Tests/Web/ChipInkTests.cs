using Pyxie.Data.Colors;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A chip's text color reads on every possible background at the 4.5:1 the design asks for, at 7:1 wherever black or white would, and otherwise as well as black or white can; and it pairs a background the way the design pairs its tints with their text.</summary>
public sealed class ChipInkTests
{
	private const double PreferredContrast = 7.0;

	private static readonly HexColor s_black = new(0, 0, 0);
	private static readonly HexColor s_white = new(255, 255, 255);

	[Fact]
	public void For_NeverReadsBelowTheDesignsMinimum()
	{
		List<string> violations = [];
		foreach (HexColor background in Backgrounds())
		{
			HexColor ink = ChipInk.For(background);
			double contrast = ContrastRatio.Between(ink, background);
			if (contrast < ContrastRatio.MinimumForText)
			{
				violations.Add($"{ink} on {background} reads at {contrast:F3}:1");
			}
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Take(20)));
	}

	[Fact]
	public void For_ReachesSevenToOneWhereverBlackOrWhiteWould()
	{
		List<string> violations = [];
		foreach (HexColor background in Backgrounds().Where(background => BestPoleContrast(background) >= PreferredContrast))
		{
			HexColor ink = ChipInk.For(background);
			double contrast = ContrastRatio.Between(ink, background);
			if (contrast < PreferredContrast)
			{
				violations.Add($"{ink} on {background} reads at {contrast:F3}:1");
			}
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Take(20)));
	}

	[Fact]
	public void For_GivesTheBetterOfBlackAndWhiteWhereNeitherReachesSevenToOne()
	{
		List<string> violations = [];
		foreach (HexColor background in Backgrounds().Where(background => BestPoleContrast(background) < PreferredContrast))
		{
			HexColor ink = ChipInk.For(background);
			HexColor better = ContrastRatio.Between(s_black, background) >= ContrastRatio.Between(s_white, background) ? s_black : s_white;
			if (ink != better)
			{
				violations.Add($"{background} got {ink}, not {better}");
			}
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Take(20)));
	}

	[Theory]
	[InlineData("#EDEDE9", "#4A4C53")]
	[InlineData("#EAF3E2", "#2F5C29")]
	[InlineData("#FBE9EC", "#8E2F42")]
	public void For_PairsTheDesignsTintsWithTextCloseToTheDesignsOwn(string tint, string designInk)
	{
		HexColor ink = ChipInk.For(HexColor.Parse(tint));

		double difference = Distance(OklchColor.From(ink), OklchColor.From(HexColor.Parse(designInk)));
		Assert.True(difference <= 0.03, $"{ink} on {tint} is {difference:F4} from the design's {designInk} in OKLab.");
	}

	[Theory]
	[InlineData("#E3F0FF")]
	[InlineData("#FFE8CC")]
	[InlineData("#EDE4FF")]
	[InlineData("#FBE9EC")]
	[InlineData("#00FF00")]
	public void For_GivesALightBackgroundDeepTextOfItsOwnHue(string written)
	{
		HexColor background = HexColor.Parse(written);
		OklchColor ink = OklchColor.From(ChipInk.For(background));

		double hueDifference = Math.Abs(Math.IEEERemainder(ink.Hue - OklchColor.From(background).Hue, 2 * Math.PI)) * 180 / Math.PI;
		Assert.True(hueDifference <= 6, $"The text on {written} is {hueDifference:F1} degrees from its hue.");
		Assert.True(ink.Lightness <= 0.45, $"The text on {written} is at lightness {ink.Lightness:F3}, not deep.");
	}

	[Theory]
	[InlineData("#000000")]
	[InlineData("#3A3B42")]
	[InlineData("#1F3A93")]
	[InlineData("#2F5C29")]
	[InlineData("#0000FF")]
	public void For_GivesADarkBackgroundPaleText(string written)
	{
		OklchColor ink = OklchColor.From(ChipInk.For(HexColor.Parse(written)));

		Assert.True(ink.Lightness >= 0.94, $"The text on {written} is at lightness {ink.Lightness:F3}, not pale.");
	}

	[Theory]
	[InlineData("#EDEDE9")]
	[InlineData("#F6F6F3")]
	[InlineData("#FFFEFF")]
	[InlineData("#FFFFFF")]
	[InlineData("#3A3B42")]
	public void For_GivesANearlyGrayBackgroundNearlyGrayText(string written)
	{
		OklchColor ink = OklchColor.From(ChipInk.For(HexColor.Parse(written)));

		Assert.True(ink.Chroma <= 0.012, $"The text on {written} has chroma {ink.Chroma:F4}, a visible hue.");
	}

	private static double BestPoleContrast(HexColor background)
	{
		return Math.Max(ContrastRatio.Between(s_black, background), ContrastRatio.Between(s_white, background));
	}

	private static double Distance(OklchColor first, OklchColor second)
	{
		double greenRed = (first.Chroma * Math.Cos(first.Hue)) - (second.Chroma * Math.Cos(second.Hue));
		double blueYellow = (first.Chroma * Math.Sin(first.Hue)) - (second.Chroma * Math.Sin(second.Hue));

		return Math.Sqrt(Math.Pow(first.Lightness - second.Lightness, 2) + (greenRed * greenRed) + (blueYellow * blueYellow));
	}

	// Every 8th value of each channel and 255, every gray, and colors around the luminance where black and white tie at 4.58:1, the hardest place to read.
	private static IEnumerable<HexColor> Backgrounds()
	{
		int[] values = [.. Enumerable.Range(0, 32).Select(step => step * 8), 255];
		foreach (int red in values)
		{
			foreach (int green in values)
			{
				foreach (int blue in values)
				{
					yield return new HexColor((byte)red, (byte)green, (byte)blue);
				}
			}
		}

		for (int gray = 0; gray < 256; gray++)
		{
			yield return new HexColor((byte)gray, (byte)gray, (byte)gray);
		}

		foreach (string written in new[] { "#767676", "#757575", "#777777", "#8060D8", "#AC30FC", "#38860A", "#D04060", "#1080A0", "#FF0000", "#1677FF", "#6FA34A", "#3D7336" })
		{
			yield return HexColor.Parse(written);
		}
	}
}
