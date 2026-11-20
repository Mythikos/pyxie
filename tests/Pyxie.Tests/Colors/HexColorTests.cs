using Pyxie.Data.Colors;

using Xunit;

namespace Pyxie.Tests.Colors;

/// <summary>A color is read only when it is written <c>#RRGGBB</c>, in either case, as a browser's color input reports it, and is always written the one way: upper case.</summary>
public sealed class HexColorTests
{
	[Theory]
	[InlineData("#eaf3e2", 0xEA, 0xF3, 0xE2)]
	[InlineData("#EAF3E2", 0xEA, 0xF3, 0xE2)]
	[InlineData("#0a1B2c", 0x0A, 0x1B, 0x2C)]
	[InlineData("#000000", 0x00, 0x00, 0x00)]
	[InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
	public void TryParse_ReadsAColorWrittenRrGgBbInEitherCase(string text, byte red, byte green, byte blue)
	{
		Assert.True(HexColor.TryParse(text, out HexColor color), $"'{text}' is a color.");
		Assert.Equal(new HexColor(red, green, blue), color);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("#")]
	[InlineData("EAF3E2")]
	[InlineData("#EAF3E")]
	[InlineData("#fff")]
	[InlineData("#EAF3E2F")]
	[InlineData("#EAF3E2FF")]
	[InlineData(" #EAF3E2")]
	[InlineData("#EAF3E2 ")]
	[InlineData("#GGGGGG")]
	[InlineData("#+12345")]
	[InlineData("#-12345")]
	[InlineData("##AF3E2")]
	[InlineData("#ＥＡＦ３Ｅ２")]
	[InlineData("#١٢٣٤٥٦")]
	[InlineData("#EDEDED;color:red")]
	[InlineData("#EDED\"><b>")]
	[InlineData("red")]
	[InlineData("rgb(1, 2, 3)")]
	public void TryParse_RefusesAnythingElse(string? text)
	{
		Assert.False(HexColor.TryParse(text, out HexColor color), $"'{text}' is not a color written #RRGGBB.");
		Assert.Equal(default, color);
	}

	[Fact]
	public void Parse_OfAnythingElse_Throws()
	{
		Assert.Throws<FormatException>(() => HexColor.Parse("#EDEDED;color:red"));
	}

	[Theory]
	[InlineData(0xEA, 0xF3, 0xE2, "#EAF3E2")]
	[InlineData(0x0A, 0x1B, 0x2C, "#0A1B2C")]
	[InlineData(0x00, 0x00, 0x00, "#000000")]
	public void ToString_WritesRrGgBbInUpperCase(byte red, byte green, byte blue, string expected)
	{
		Assert.Equal(expected, new HexColor(red, green, blue).ToString());
	}

	[Theory]
	[InlineData("#eaf3e2")]
	[InlineData("#1f3a93")]
	public void ToString_OfAParsedColor_ReadsBackAsTheSameColor(string text)
	{
		HexColor color = HexColor.Parse(text);

		Assert.Equal(text.ToUpperInvariant(), color.ToString());
		Assert.Equal(color, HexColor.Parse(color.ToString()));
	}
}
