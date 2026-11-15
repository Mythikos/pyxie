using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Categories;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CategoryChipTests
{
	[Theory]
	[InlineData(CategoryColor.Neutral, "pyx-chip")]
	[InlineData(CategoryColor.Green, "pyx-chip pyx-chip-green")]
	[InlineData(CategoryColor.Pink, "pyx-chip pyx-chip-pink")]
	public void Of_GivesEachColorItsChipClasses(CategoryColor color, string expected)
	{
		Assert.Equal(expected, CategoryChipClasses.Of(color));
	}

	[Fact]
	public void Of_GivesEveryDefinedColorAChip()
	{
		foreach (CategoryColor color in Enum.GetValues<CategoryColor>())
		{
			Assert.StartsWith("pyx-chip", CategoryChipClasses.Of(color), StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Of_ShowsAnUndefinedColorAsNeutral()
	{
		Assert.Equal("pyx-chip", CategoryChipClasses.Of((CategoryColor)999));
	}

	[Fact]
	public async Task Render_ShowsTheCategorysNameInItsColor()
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CategoryChip> chip = await harness.RenderAsync<CategoryChip>(new Dictionary<string, object> { [nameof(CategoryChip.Category)] = new Category { Id = 2, Name = "Bug", Color = CategoryColor.Pink } });

		string html = await chip.HtmlAsync();

		Assert.Matches(@"<span class=""pyx-chip pyx-chip-pink"" title=""Bug""[^>]*><span class=""chip-name""[^>]*>Bug</span></span>", html);
	}

	[Fact]
	public async Task Render_GivesTheFullNameAsTheTitleAndKeepsTheNameInItsOwnElementSoALongOneCanBeCutShort()
	{
		string name = new('x', PyxieLimits.MaxNameLength);
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CategoryChip> chip = await harness.RenderAsync<CategoryChip>(new Dictionary<string, object> { [nameof(CategoryChip.Category)] = new Category { Id = 2, Name = name, Color = CategoryColor.Green } });

		string html = await chip.HtmlAsync();

		Assert.Matches($@"<span class=""pyx-chip pyx-chip-green"" title=""{name}""[^>]*><span class=""chip-name""[^>]*>{name}</span></span>", html);
	}
}
