using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The category panel does each thing once. Its controls are plain elements and a form, whose disabled state only the browser keeps, so a second submit or click reaches the server when the person pressed again before the browser showed the result: while the first one's work runs it must not start another, or write a color chosen after the press, and after it ended it must not ask for a blank name after an add, or for a category that is already deleted.</summary>
public sealed class CategoryListDoubleSubmitTests
{
	[Fact]
	public async Task Add_SubmittedAgainAfterTheCategoryWasAdded_AsksForNothing()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		GateRenderedElement form = await AddFormAsync(host);
		await host.HoldAcknowledgementsAsync();

		await host.SubmitAsync(form);
		await host.SubmitAsync(form);
		await host.AcknowledgeAsync();

		Assert.Single(await ReadAsync(database), category => category.Name == "Docs");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Add_SubmittedAgainWhileTheFirstAddRuns_AddsOnce()
	{
		await using TestDatabase database = new();
		GatedCategoryService categories = new(new CategoryService(database.ContextFactory));
		categories.Creates.Hold(1);
		await using ServiceProvider services = GateRenderHost.CreateServices(database, categoryService: categories);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		GateRenderedElement form = await AddFormAsync(host);

		Task first = host.SubmitAsync(form);
		await categories.Creates.WaitForStartAsync(1);
		await host.SubmitAsync(form);
		categories.Creates.Release(1);
		await first;

		Assert.Equal(1, categories.Creates.Started);
		Assert.Single(await ReadAsync(database), category => category.Name == "Docs");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Add_OfABlankNameTypedAfterAnAdd_ShowsTheServicesMessage()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		await host.SubmitAsync(await AddFormAsync(host));
		await host.TypeAsync(await NewFieldAsync(host), "D");
		await host.TypeAsync(await NewFieldAsync(host), string.Empty);

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(["Category name is required."], await host.AlertsAsync());
		Assert.Equal(4, (await ReadAsync(database)).Count);
	}

	[Fact]
	public async Task Add_WritesTheColorItWasSubmittedWith_WhenAnotherIsChosenWhileTheAddRuns()
	{
		await using TestDatabase database = new();
		GatedCategoryService categories = new(new CategoryService(database.ContextFactory));
		categories.Creates.Hold(1);
		await using ServiceProvider services = GateRenderHost.CreateServices(database, categoryService: categories);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		await host.SetCheckedAsync(await host.FindRadioAsync("Green"), true);
		GateRenderedElement well = await NewWellAsync(host);
		await host.HoldAcknowledgementsAsync();

		Task add = host.SubmitAsync(await AddFormAsync(host));
		await categories.Creates.WaitForStartAsync(1);

		// A color the browser's picker moved to before the browser showed the form busy arrives while the add runs.
		await host.TypeAsync(well, "#1f3a93");
		categories.Creates.Release(1);
		await add;
		await host.AcknowledgeAsync();

		Assert.Equal(CategoryColors.Green, Assert.Single(await ReadAsync(database), category => category.Name == "Docs").Color);
	}

	[Fact]
	public async Task Add_OfABlankNameAfterAnAddAndAColorChoice_ShowsTheServicesMessage()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.TypeAsync(await NewFieldAsync(host), "Docs");
		await host.SubmitAsync(await AddFormAsync(host));
		await host.SetCheckedAsync(await host.FindRadioAsync("Pink"), true);

		await host.SubmitAsync(await AddFormAsync(host));

		Assert.Equal(["Category name is required."], await host.AlertsAsync());
		Assert.Equal(4, (await ReadAsync(database)).Count);
	}

	[Fact]
	public async Task Delete_PressedAgainAfterTheCategoryWasDeleted_ShowsNoError()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		GateRenderedElement delete = await host.FindButtonAsync("Delete Chore");
		await host.HoldAcknowledgementsAsync();

		await host.ClickAsync(delete);
		await host.ClickAsync(delete);
		await host.AcknowledgeAsync();

		Assert.DoesNotContain(await ReadAsync(database), category => category.Name == "Chore");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Delete_DoubleClicked_DeletesOnlyTheCategoryClickedFirst()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Delete Bug"));

		// Chore slid into Bug's place, under the pointer, before the second click of the double click.
		await host.ClickAsync(await host.FindButtonAsync("Delete Chore"), clicks: 2);

		Assert.Equal(["Chore", "Feature"], (await ReadAsync(database)).Select(category => category.Name).Order());
		Assert.Empty(await host.AlertsAsync());
	}

	private static async Task<GateRenderedElement> NewFieldAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("id") == "new-category"));
	}

	private static async Task<GateRenderedElement> NewWellAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("type") == "color" && input.Attribute("aria-label") == "Custom color"));
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
