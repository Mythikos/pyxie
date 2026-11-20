using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Categories.Colors;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CategoryListTests
{
	[Theory]
	[InlineData("Feature", "pyx-chip pyx-chip-green")]
	[InlineData("Bug", "pyx-chip pyx-chip-pink")]
	[InlineData("Chore", "pyx-chip")]
	public async Task Render_ShowsEveryCategoryAsAChipInItsColor(string category, string expectedClasses)
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<CategoryList>(database);

		Assert.Matches($@"<span class=""category-chip {expectedClasses}""[^>]*>\s*<button\b[^>]*aria-label=""Edit {category}""[^>]*>{category}</button>", html);
	}

	[Fact]
	public async Task Render_ShowsACategoryInAColorOfItsOwnAsAChipInThatColor()
	{
		HexColor color = HexColor.Parse("#E3F0FF");
		await using TestDatabase database = new();
		await new CategoryService(database.ContextFactory).CreateAsync("Docs", color, TestContext.Current.CancellationToken);

		string html = await GateRenderHost.RenderAsync<CategoryList>(database);

		Assert.Matches($@"<span class=""category-chip pyx-chip pyx-chip-custom"" style=""--chip-tint: #E3F0FF; --chip-ink: {ChipInk.For(color)}""[^>]*>\s*<button\b[^>]*aria-label=""Edit Docs""[^>]*>Docs</button>", html);
	}

	[Fact]
	public async Task Render_IsThePanelTheCategoriesAnchorPointsAt()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<CategoryList>(database);

		Assert.Matches(@"<section id=""categories"" class=""pyx-panel[^""]*"" aria-labelledby=""categories-heading""", html);
		Assert.Matches(@"<h2 id=""categories-heading""[^>]*>Categories</h2>", html);
	}

	[Fact]
	public async Task Render_NamesEveryChipButtonAfterItsCategoryAndTheAddFieldAfterItsPurpose()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		foreach (string category in new[] { "Bug", "Feature", "Chore" })
		{
			await host.FindButtonAsync($"Edit {category}");
			await host.FindButtonAsync($"Delete {category}");
		}

		GateRenderedElement field = Assert.Single(await host.FindElementsAsync("input"), input => input.Attribute("id") == "new-category");
		Assert.Equal("New category", field.Attribute("placeholder"));
		Assert.Matches(@"<label for=""new-category"" class=""pyx-visually-hidden""[^>]*>New category</label>", await HtmlOfAsync(database));
	}

	[Fact]
	public async Task Render_ShowsTheRemoveButtonsAsIconsOnlyAndAddAsAWord()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		Assert.Equal(string.Empty, (await host.FindButtonAsync("Delete Bug")).Text);
		Assert.Equal("Add", Assert.Single(await host.FindElementsAsync("button", button => button.Attribute("type") == "submit")).Text);
	}

	[Fact]
	public async Task Render_WithoutCategories_ShowsOnlyTheHeadingAndTheAddForm()
	{
		await using TestDatabase database = new();
		await using (ProjectDbContext context = database.CreateContext())
		{
			await context.Categories.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
		}

		string html = await GateRenderHost.RenderAsync<CategoryList>(database);

		Assert.Matches(@"<h2 id=""categories-heading""[^>]*>Categories</h2>", html);
		Assert.DoesNotMatch(@"aria-label=""Edit ", html);
		Assert.Contains("id=\"new-category\"", html, StringComparison.Ordinal);
	}

	private static async Task<string> HtmlOfAsync(TestDatabase database)
	{
		return Regex.Replace(await GateRenderHost.RenderAsync<CategoryList>(database), @"\s+", " ");
	}
}
