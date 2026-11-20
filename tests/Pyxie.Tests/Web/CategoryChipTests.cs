using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Categories;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A chip in a quick color is drawn with that color's shared classes, so the design's tints keep the design's text colors; a chip in any other color is drawn in it, with a text color derived for it, through custom properties written from the parsed color.</summary>
public sealed class CategoryChipTests
{
	[Theory]
	[InlineData("#EDEDE9", "pyx-chip")]
	[InlineData("#EAF3E2", "pyx-chip pyx-chip-green")]
	[InlineData("#eaf3e2", "pyx-chip pyx-chip-green")]
	[InlineData("#FBE9EC", "pyx-chip pyx-chip-pink")]
	public void Of_GivesEachQuickColorItsClassesAndNoStyle(string color, string expected)
	{
		Assert.Equal(new CategoryChipAppearance(expected, null), CategoryChipAppearance.Of(HexColor.Parse(color)));
	}

	[Theory]
	[InlineData("#E3F0FF")]
	[InlineData("#EDEDEA")]
	[InlineData("#1F3A93")]
	[InlineData("#000000")]
	[InlineData("#FFFFFF")]
	public void Of_DrawsAnyOtherColorAsACustomChipInThatColorWithItsDerivedText(string written)
	{
		HexColor color = HexColor.Parse(written);

		CategoryChipAppearance appearance = CategoryChipAppearance.Of(color);

		Assert.Equal(new CategoryChipAppearance("pyx-chip pyx-chip-custom", $"--chip-tint: {written}; --chip-ink: {ChipInk.For(color)}"), appearance);
		Assert.Matches("^--chip-tint: #[0-9A-F]{6}; --chip-ink: #[0-9A-F]{6}$", appearance.Style);
	}

	[Fact]
	public async Task Render_ShowsTheCategorysNameInItsColor()
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CategoryChip> chip = await harness.RenderAsync<CategoryChip>(new Dictionary<string, object> { [nameof(CategoryChip.Category)] = new Category { Id = 2, Name = "Bug", Color = CategoryColors.Pink } });

		string html = await chip.HtmlAsync();

		Assert.Matches(@"<span class=""pyx-chip pyx-chip-pink"" title=""Bug""[^>]*><span class=""chip-name""[^>]*>Bug</span></span>", html);
		Assert.DoesNotContain("style=", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsACategoryInAColorOfItsOwnThroughItsStyle()
	{
		HexColor color = HexColor.Parse("#1F3A93");
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CategoryChip> chip = await harness.RenderAsync<CategoryChip>(new Dictionary<string, object> { [nameof(CategoryChip.Category)] = new Category { Id = 4, Name = "Docs", Color = color } });

		string html = await chip.HtmlAsync();

		Assert.Matches($@"<span class=""pyx-chip pyx-chip-custom"" title=""Docs"" style=""--chip-tint: #1F3A93; --chip-ink: {ChipInk.For(color)}""[^>]*><span class=""chip-name""[^>]*>Docs</span></span>", html);
	}

	[Fact]
	public async Task Render_GivesTheFullNameAsTheTitleAndKeepsTheNameInItsOwnElementSoALongOneCanBeCutShort()
	{
		string name = new('x', PyxieLimits.MaxNameLength);
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CategoryChip> chip = await harness.RenderAsync<CategoryChip>(new Dictionary<string, object> { [nameof(CategoryChip.Category)] = new Category { Id = 2, Name = name, Color = CategoryColors.Green } });

		string html = await chip.HtmlAsync();

		Assert.Matches($@"<span class=""pyx-chip pyx-chip-green"" title=""{name}""[^>]*><span class=""chip-name""[^>]*>{name}</span></span>", html);
	}
}
