using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Categories.Colors;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A category is deleted by its chip's remove button at once, and added with the form after the chips, in the color chosen there; the service's refusals show above the chips.</summary>
public sealed class CategoryListChangeTests
{
	[Fact]
	public async Task Delete_RemovesTheCategoryAtOnceWithoutAskingAndLeavesTheOthers()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Delete Chore"));

		Assert.Equal(["Bug", "Feature"], (await ReadAsync(database)).Select(category => category.Name));
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") is "Edit Chore" or "Delete Chore");
		await host.FindButtonAsync("Edit Bug");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Delete_OfACategoryCardsUse_ShowsTheServicesMessageAndKeepsTheChip()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Fix it", categoryId: PyxieSeedData.BugCategoryId);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Delete Bug"));

		Assert.Equal(["Category 'Bug' is used by 1 card and cannot be deleted."], await host.AlertsAsync());
		await host.FindButtonAsync("Edit Bug");
		Assert.Contains(await ReadAsync(database), category => category.Name == "Bug");
	}

	[Fact]
	public async Task TheMessageOfARefusedDeleteGoesAwayWithTheNextChange()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Fix it", categoryId: PyxieSeedData.BugCategoryId);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Delete Bug"));
		Assert.Single(await host.AlertsAsync());

		await host.ClickAsync(await host.FindButtonAsync("Delete Chore"));

		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Add_CreatesANeutralCategoryAfterTrimmingItsNameAndClearsTheField()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "  Docs  ");

		await host.SubmitAsync(await AddFormAsync(host));

		Category added = Assert.Single(await ReadAsync(database), category => category.Name == "Docs");
		Assert.Equal(CategoryColors.Neutral, added.Color);
		await host.FindButtonAsync("Edit Docs");
		Assert.Equal(string.Empty, (await NewFieldAsync(host)).Attribute("value") ?? string.Empty);
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Add_WithTheNameOfAnExistingCategory_ShowsTheMessageAndKeepsWhatWasTyped()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "bug");

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(["A category named 'bug' already exists."], await host.AlertsAsync());
		Assert.Equal("bug", (await NewFieldAsync(host)).Attribute("value"));
		Assert.Equal(3, (await ReadAsync(database)).Count);
	}

	[Fact]
	public async Task Add_WithABlankName_ShowsTheServicesMessage()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(["Category name is required."], await host.AlertsAsync());
		Assert.Equal(3, (await ReadAsync(database)).Count);
	}

	[Fact]
	public async Task Add_OffersTheColorChoiceForTheNewCategoryWithNeutralChosen()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		Assert.Single(await host.FindElementsAsync("div", div => div.Attribute("role") == "group" && div.Attribute("aria-label") == "New category color"));
		Assert.Equal(["true", null, null], (await AddFormSwatchesAsync(host)).Select(radio => radio.Attribute("checked")));
		Assert.Equal("#edede9", (await WellAsync(host)).Attribute("value"));
	}

	[Fact]
	public async Task Add_InAQuickColor_CreatesTheCategoryInIt()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		await host.SetCheckedAsync(await host.FindRadioAsync("Pink"), true);

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(CategoryColors.Pink, Assert.Single(await ReadAsync(database), category => category.Name == "Docs").Color);
		Assert.Equal(2, (await host.FindElementsAsync("span", span => span.Attribute("class") == "category-chip pyx-chip pyx-chip-pink")).Count);
	}

	[Fact]
	public async Task Add_InAColorPicked_CreatesTheCategoryInItAndShowsTheChipInIt()
	{
		HexColor color = HexColor.Parse("#1F3A93");
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		await host.TypeAsync(await WellAsync(host), "#1f3a93");

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(color, Assert.Single(await ReadAsync(database), category => category.Name == "Docs").Color);
		GateRenderedElement chip = Assert.Single(await host.FindElementsAsync("span", span => span.Attribute("class") == "category-chip pyx-chip pyx-chip-custom"));
		Assert.Equal($"--chip-tint: #1F3A93; --chip-ink: {ChipInk.For(color)}", chip.Attribute("style"));
	}

	[Fact]
	public async Task Add_ThatWentThrough_PutsTheColorChoiceBackToNeutral()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		await host.TypeAsync(await WellAsync(host), "#1f3a93");

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal("true", (await host.FindRadioAsync("Neutral")).Attribute("checked"));
		Assert.Equal("#edede9", (await WellAsync(host)).Attribute("value"));
	}

	[Fact]
	public async Task Add_RefusedForItsName_KeepsTheColorChosen()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "bug");
		await host.TypeAsync(await WellAsync(host), "#1f3a93");

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(["A category named 'bug' already exists."], await host.AlertsAsync());
		Assert.Equal("#1f3a93", (await WellAsync(host)).Attribute("value"));
		Assert.All(await AddFormSwatchesAsync(host), radio => Assert.Null(radio.Attribute("checked")));
	}

	private static Task<IReadOnlyList<GateRenderedElement>> AddFormSwatchesAsync(GateInteractiveHost host)
	{
		return host.FindElementsAsync("input", input => input.Attribute("type") == "radio" && input.Attribute("name") == "New category color");
	}

	private static async Task<GateRenderedElement> WellAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("type") == "color" && input.Attribute("aria-label") == "Custom color"));
	}

	private static async Task<GateRenderedElement> NewFieldAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("id") == "new-category"));
	}

	private static async Task<GateRenderedElement> AddFormAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("form", form => form.Attribute("class") == "category-add"));
	}

	private static Task<IReadOnlyList<Category>> ReadAsync(TestDatabase database)
	{
		return new CategoryService(database.ContextFactory).ListAsync(TestContext.Current.CancellationToken);
	}
}
