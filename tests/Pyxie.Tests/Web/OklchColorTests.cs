using Pyxie.Data.Colors;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>OKLCH is Ottosson's OKLab in polar form: it agrees with the published values, gives every sRGB color back unchanged, and knows where sRGB ends at each lightness and hue.</summary>
public sealed class OklchColorTests
{
	[Theory]
	[InlineData("#FFFFFF", 1.0, 0.0)]
	[InlineData("#000000", 0.0, 0.0)]
	[InlineData("#FF0000", 0.6280, 0.2577)]
	[InlineData("#00FF00", 0.8664, 0.2948)]
	[InlineData("#0000FF", 0.4520, 0.3132)]
	public void From_GivesThePublishedLightnessAndChroma(string color, double lightness, double chroma)
	{
		OklchColor converted = OklchColor.From(HexColor.Parse(color));

		Assert.Equal(lightness, converted.Lightness, 3);
		Assert.Equal(chroma, converted.Chroma, 3);
	}

	[Fact]
	public void From_GivesThePublishedHueOfRed()
	{
		Assert.Equal(29.23, OklchColor.From(HexColor.Parse("#FF0000")).Hue * 180 / Math.PI, 1);
	}

	[Fact]
	public void ToHexColor_GivesEverySrgbColorBackUnchanged()
	{
		List<string> changed = [];
		foreach (HexColor color in Grid())
		{
			HexColor back = OklchColor.From(color).ToHexColor();
			if (back != color)
			{
				changed.Add($"{color} came back as {back}");
			}
		}

		Assert.True(changed.Count == 0, string.Join(Environment.NewLine, changed.Take(20)));
	}

	[Theory]
	[InlineData(0.44, 0.5)]
	[InlineData(0.44, 2.4)]
	[InlineData(0.95, 1.0)]
	[InlineData(0.70, -1.2)]
	public void MaxChroma_IsTheEdgeOfSrgb(double lightness, double hue)
	{
		double maxChroma = OklchColor.MaxChroma(lightness, hue);

		Assert.True(maxChroma > 0, "Every lightness between black and white holds some chroma.");
		Assert.True(new OklchColor(lightness, maxChroma, hue).IsInGamut(), $"Chroma {maxChroma} at L {lightness} is inside sRGB.");
		Assert.False(new OklchColor(lightness, maxChroma + 0.001, hue).IsInGamut(), $"Chroma {maxChroma + 0.001} at L {lightness} is outside sRGB.");
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(1.0)]
	public void MaxChroma_AtBlackAndWhite_IsNone(double lightness)
	{
		double maxChroma = OklchColor.MaxChroma(lightness, 1.0);

		Assert.True(maxChroma < 0.005, $"Chroma {maxChroma} at L {lightness}: black and white hold no color.");
		Assert.Equal(lightness == 0 ? new HexColor(0, 0, 0) : new HexColor(255, 255, 255), new OklchColor(lightness, maxChroma, 1.0).ToHexColor());
	}

	private static IEnumerable<HexColor> Grid()
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
	}
}
