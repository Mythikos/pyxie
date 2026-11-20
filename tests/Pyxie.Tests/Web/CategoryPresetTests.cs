using System.Text.RegularExpressions;

using Pyxie.Data.Colors;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Categories;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The quick colors are the design's chip tints, kept in step with the tokens in app.css, and each one's chip classes pair it with the design's text color for it, which reads on it at 4.5:1 or more.</summary>
public sealed class CategoryPresetTests
{
	[Fact]
	public void All_OffersNeutralGreenAndPinkInThatOrder()
	{
		Assert.Equal([("Neutral", CategoryColors.Neutral, "pyx-chip"), ("Green", CategoryColors.Green, "pyx-chip pyx-chip-green"), ("Pink", CategoryColors.Pink, "pyx-chip pyx-chip-pink")], CategoryPreset.All.Select(preset => (preset.Name, preset.Color, preset.ChipClasses)));
	}

	[Theory]
	[InlineData("#EDEDE9", "Neutral")]
	[InlineData("#eaf3e2", "Green")]
	[InlineData("#FBE9EC", "Pink")]
	[InlineData("#EDEDEA", null)]
	[InlineData("#1F3A93", null)]
	public void Find_GivesTheQuickColorAColorIs(string color, string? expected)
	{
		Assert.Equal(expected, CategoryPreset.Find(HexColor.Parse(color))?.Name);
	}

	[Theory]
	[InlineData("Neutral", "--pyx-lane", "--pyx-text-2")]
	[InlineData("Green", "--pyx-success-tint", "--pyx-primary-hover")]
	[InlineData("Pink", "--pyx-pink-tint", "--pyx-pink-text")]
	public void EachQuickColor_IsItsTokenAndReadsWithItsTextToken(string name, string tintToken, string inkToken)
	{
		string css = File.ReadAllText(Path.Combine(RepositoryLayout.FindRoot(), "src", "Pyxie.Web", "wwwroot", "app.css"));
		CategoryPreset preset = Assert.Single(CategoryPreset.All, preset => preset.Name == name);

		HexColor tint = Token(css, tintToken);
		HexColor ink = Token(css, inkToken);

		Assert.Equal(tint, preset.Color);
		Assert.True(ContrastRatio.Between(ink, tint) >= ContrastRatio.MinimumForText, $"{inkToken} reads on {tintToken} at {ContrastRatio.Between(ink, tint):F2}:1.");
	}

	private static HexColor Token(string css, string token)
	{
		Match declaration = Regex.Match(css, $@"{Regex.Escape(token)}:\s*(#[0-9A-Fa-f]{{6}});");
		Assert.True(declaration.Success, $"app.css declares {token} as #RRGGBB.");

		return HexColor.Parse(declaration.Groups[1].Value);
	}
}
