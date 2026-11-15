using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A category is deleted by its chip's remove button at once, and added with the form after the chips; the service's refusals show above the chips.</summary>
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
		Assert.Equal(CategoryColor.Neutral, added.Color);
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
