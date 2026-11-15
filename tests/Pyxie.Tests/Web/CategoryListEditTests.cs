using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A chip's name opens an editor in the chip's place, for the name and the color. Save writes what changed, and a refusal stays in the editor with what was typed.</summary>
public sealed class CategoryListEditTests
{
	[Fact]
	public async Task Edit_TurnsTheChipIntoAnEditorNamedAfterTheCategoryAndLeavesTheOtherChips()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		Assert.Equal("Bug", (await NameFieldAsync(host, "Bug")).Attribute("value"));
		await host.FindButtonAsync("Save Bug");
		await host.FindButtonAsync("Cancel editing Bug");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") is "Edit Bug" or "Delete Bug");
		await host.FindButtonAsync("Edit Feature");
		await host.FindButtonAsync("Delete Chore");
	}

	[Fact]
	public async Task Edit_OffersTheThreeColorsAsOneChoiceWithTheCategorysColorChosen()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		IReadOnlyList<GateRenderedElement> swatches = await host.FindElementsAsync("input", input => input.Attribute("type") == "radio");
		Assert.Equal(["Neutral", "Green", "Pink"], swatches.Select(swatch => swatch.Attribute("aria-label")));
		Assert.Single(swatches.Select(swatch => swatch.Attribute("name")).Distinct());
		Assert.Equal([null, null, "true"], swatches.Select(swatch => swatch.Attribute("checked")));
		GateRenderedElement group = Assert.Single(await host.FindElementsAsync("div", div => div.Attribute("role") == "radiogroup"));
		Assert.Equal("Color", group.Attribute("aria-label"));
	}

	[Fact]
	public async Task Edit_ShowsTheChosenColorOnTheEditorAtOnce()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Chore"));
		Assert.Equal(["category-editor", "pyx-chip"], (await EditorAsync(host)).Attribute("class")!.Split(' '));

		await host.SetCheckedAsync(await host.FindRadioAsync("Green"), true);

		Assert.Equal(["category-editor", "pyx-chip", "pyx-chip-green"], (await EditorAsync(host)).Attribute("class")!.Split(' '));
		Assert.Equal("true", (await host.FindRadioAsync("Green")).Attribute("checked"));
		Assert.Null((await host.FindRadioAsync("Neutral")).Attribute("checked"));
	}

	[Fact]
	public async Task Save_AfterChangingOnlyTheName_RenamesTheCategoryAndKeepsItsColor()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "  Defect  ");

		await host.SubmitAsync(await EditorAsync(host));

		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.BugCategoryId);
		Assert.Equal(("Defect", CategoryColor.Pink), (stored.Name, stored.Color));
		await host.FindButtonAsync("Edit Defect");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") == "Save Bug");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Save_AfterChangingOnlyTheColor_RecolorsTheCategoryAndKeepsItsName()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Chore"));
		await host.SetCheckedAsync(await host.FindRadioAsync("Pink"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Category stored = (await ReadAsync(database)).Single(category => category.Name == "Chore");
		Assert.Equal(CategoryColor.Pink, stored.Color);
		IReadOnlyList<GateRenderedElement> chips = await host.FindElementsAsync("span", span => (span.Attribute("class") ?? string.Empty).StartsWith("category-chip", StringComparison.Ordinal));
		Assert.Equal(3, chips.Count);
		Assert.Equal(["category-chip pyx-chip pyx-chip-pink", "category-chip pyx-chip pyx-chip-pink", "category-chip pyx-chip pyx-chip-green"], chips.Select(chip => chip.Attribute("class")));
	}

	[Fact]
	public async Task Save_AfterChangingTheNameAndTheColor_WritesBoth()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Feature"));
		await host.TypeAsync(await NameFieldAsync(host, "Feature"), "Story");
		await host.SetCheckedAsync(await host.FindRadioAsync("Neutral"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.FeatureCategoryId);
		Assert.Equal(("Story", CategoryColor.Neutral), (stored.Name, stored.Color));
	}

	[Fact]
	public async Task Save_WithNothingChanged_WritesNothingAndClosesTheEditor()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(["Bug", "Chore", "Feature"], (await ReadAsync(database)).Select(category => category.Name));
		await host.FindButtonAsync("Edit Bug");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task Save_WithTheNameOfAnotherCategory_StaysInTheEditorWithWhatWasTypedAndShowsTheMessage()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "feature");
		await host.SetCheckedAsync(await host.FindRadioAsync("Green"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(["A category named 'feature' already exists."], await host.AlertsAsync());
		Assert.Equal("feature", (await NameFieldAsync(host, "Bug")).Attribute("value"));
		Assert.Equal("true", (await host.FindRadioAsync("Green")).Attribute("checked"));
		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.BugCategoryId);
		Assert.Equal(("Bug", CategoryColor.Pink), (stored.Name, stored.Color));
	}

	[Fact]
	public async Task Save_WithABlankName_ShowsTheServicesMessageAndWritesNothing()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "   ");

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(["Category name is required."], await host.AlertsAsync());
		Assert.Equal("Bug", (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.BugCategoryId).Name);
	}

	[Fact]
	public async Task Save_OfACategoryThatWasDeletedMeanwhile_ShowsTheMessageAndTheChipIsGone()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Chore"));
		await new CategoryService(database.ContextFactory).DeleteAsync(PyxieSeedData.ChoreCategoryId, TestContext.Current.CancellationToken);
		await host.TypeAsync(await NameFieldAsync(host, "Chore"), "Task");

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(["Category not found."], await host.AlertsAsync());
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") is "Edit Chore" or "Save Chore");
	}

	[Fact]
	public async Task Cancel_PutsTheChipBackWritesNothingAndMovesTheFocusToIt()
	{
		await using TestDatabase database = new();
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "Defect");
		await host.SetCheckedAsync(await host.FindRadioAsync("Green"), true);

		await host.ClickAsync(await host.FindButtonAsync("Cancel editing Bug"));

		GateRenderedElement chip = await host.FindButtonAsync("Edit Bug");
		Assert.Equal("Bug", chip.Text);
		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.BugCategoryId);
		Assert.Equal(("Bug", CategoryColor.Pink), (stored.Name, stored.Color));
		(string Identifier, object?[] Arguments) focus = Assert.Single(browser.Calls, call => call.Identifier == GateStubJsRuntime.FocusIdentifier);
		Assert.Equal(chip.ReferenceId, Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
	}

	[Fact]
	public async Task Save_MovesTheFocusToTheChipThatWasEdited()
	{
		await using TestDatabase database = new();
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "Defect");

		await host.SubmitAsync(await EditorAsync(host));

		GateRenderedElement chip = await host.FindButtonAsync("Edit Defect");
		(string Identifier, object?[] Arguments) focus = Assert.Single(browser.Calls, call => call.Identifier == GateStubJsRuntime.FocusIdentifier);
		Assert.Equal(chip.ReferenceId, Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
	}

	[Fact]
	public async Task Edit_ThenEditingAnotherChip_ShowsTheEditorOfTheOtherOnly()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		await host.ClickAsync(await host.FindButtonAsync("Edit Chore"));

		await NameFieldAsync(host, "Chore");
		await host.FindButtonAsync("Edit Bug");
		await EditorAsync(host);
	}

	private static async Task<GateRenderedElement> NameFieldAsync(GateInteractiveHost host, string category)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("aria-label") == $"New name for {category}"));
	}

	private static async Task<GateRenderedElement> EditorAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("form", form => (form.Attribute("class") ?? string.Empty).StartsWith("category-editor", StringComparison.Ordinal)));
	}

	private static Task<IReadOnlyList<Category>> ReadAsync(TestDatabase database)
	{
		return new CategoryService(database.ContextFactory).ListAsync(TestContext.Current.CancellationToken);
	}
}
