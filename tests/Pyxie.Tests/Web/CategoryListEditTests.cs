using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Categories.Colors;
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
	public async Task Edit_OffersTheQuickColorsAndThePickerAsOneChoiceForTheCategoryWithItsColorChosen()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		IReadOnlyList<GateRenderedElement> swatches = await host.FindElementsAsync("input", input => input.Attribute("type") == "radio" && (input.Attribute("aria-label") ?? string.Empty).EndsWith(" for Bug", StringComparison.Ordinal));
		Assert.Equal(["Neutral for Bug", "Green for Bug", "Pink for Bug"], swatches.Select(swatch => swatch.Attribute("aria-label")));
		Assert.Equal(["Color for Bug"], swatches.Select(swatch => swatch.Attribute("name")).Distinct());
		Assert.Equal([null, null, "true"], swatches.Select(swatch => swatch.Attribute("checked")));
		Assert.Equal("#fbe9ec", (await WellAsync(host, "Custom color for Bug")).Attribute("value"));
		Assert.Single(await host.FindElementsAsync("div", div => div.Attribute("role") == "group" && div.Attribute("aria-label") == "Color for Bug"));
	}

	[Fact]
	public async Task Edit_NamesEveryColorControlOnThePageApartFromTheAddForms()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		IReadOnlyList<GateRenderedElement> controls = await host.FindElementsAsync("input", input => input.Attribute("type") is "radio" or "color");
		Assert.Equal(8, controls.Count);
		Assert.Equal(8, controls.Select(control => control.Attribute("aria-label")).Distinct().Count());
		Assert.Equal(2, controls.Where(control => control.Attribute("type") == "radio").Select(control => control.Attribute("name")).Distinct().Count());
	}

	[Fact]
	public async Task Edit_OfACategoryInAColorOfItsOwn_ChecksNoQuickColorAndHoldsTheColorInThePicker()
	{
		await using TestDatabase database = new();
		await new CategoryService(database.ContextFactory).CreateAsync("Docs", HexColor.Parse("#1F3A93"), TestContext.Current.CancellationToken);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();

		await host.ClickAsync(await host.FindButtonAsync("Edit Docs"));

		foreach (string quickColor in new[] { "Neutral", "Green", "Pink" })
		{
			Assert.Null((await host.FindRadioAsync($"{quickColor} for Docs")).Attribute("checked"));
		}

		Assert.Equal("#1f3a93", (await WellAsync(host, "Custom color for Docs")).Attribute("value"));
		Assert.Equal(["category-editor", "pyx-chip", "pyx-chip-custom"], ((await EditorAsync(host)).Attribute("class") ?? string.Empty).Split(' '));
	}

	[Fact]
	public async Task Edit_PickingAColor_ShowsItOnTheEditorAtOnceAndWritesNothing()
	{
		HexColor color = HexColor.Parse("#1F3A93");
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Chore"));

		await host.TypeAsync(await WellAsync(host, "Custom color for Chore"), "#1f3a93");

		GateRenderedElement editor = await EditorAsync(host);
		Assert.Equal(["category-editor", "pyx-chip", "pyx-chip-custom"], (editor.Attribute("class") ?? string.Empty).Split(' '));
		Assert.Equal($"--chip-tint: #1F3A93; --chip-ink: {ChipInk.For(color)}", editor.Attribute("style"));
		Assert.Null((await host.FindRadioAsync("Neutral for Chore")).Attribute("checked"));
		Assert.Equal(CategoryColors.Neutral, (await ReadAsync(database)).Single(category => category.Name == "Chore").Color);
	}

	[Fact]
	public async Task Save_AfterPickingAColor_StoresItAndShowsTheChipInIt()
	{
		HexColor color = HexColor.Parse("#1F3A93");
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Chore"));
		await host.TypeAsync(await WellAsync(host, "Custom color for Chore"), "#1f3a93");

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(color, (await ReadAsync(database)).Single(category => category.Name == "Chore").Color);
		GateRenderedElement chip = Assert.Single(await host.FindElementsAsync("span", span => span.Attribute("class") == "category-chip pyx-chip pyx-chip-custom"));
		Assert.Equal($"--chip-tint: #1F3A93; --chip-ink: {ChipInk.For(color)}", chip.Attribute("style"));
	}

	[Fact]
	public async Task Save_FromAColorOfItsOwnBackToAQuickColor_StoresTheQuickColor()
	{
		await using TestDatabase database = new();
		await new CategoryService(database.ContextFactory).CreateAsync("Docs", HexColor.Parse("#1F3A93"), TestContext.Current.CancellationToken);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Docs"));
		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Docs"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(CategoryColors.Green, (await ReadAsync(database)).Single(category => category.Name == "Docs").Color);
		Assert.Empty(await host.FindElementsAsync("span", span => span.Attribute("class") == "category-chip pyx-chip pyx-chip-custom"));
	}

	[Fact]
	public async Task Save_AfterPickingTheColorTheCategoryHasInLowerCase_WritesNothing()
	{
		await using TestDatabase database = new();
		GatedCategoryService categories = new(new CategoryService(database.ContextFactory));
		await using ServiceProvider services = GateRenderHost.CreateServices(database, categoryService: categories);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await WellAsync(host, "Custom color for Bug"), "#fbe9ec");

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal((0, 0), (categories.Renames.Started, categories.Recolors.Started));
		await host.FindButtonAsync("Edit Bug");
	}

	[Fact]
	public async Task Edit_ChoosingAColor_LeavesTheAddFormsColorAsItWas()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Bug"), true);
		await host.TypeAsync(await WellAsync(host, "Custom color for Bug"), "#1f3a93");

		Assert.Equal("true", (await host.FindRadioAsync("Neutral")).Attribute("checked"));
		Assert.Equal("#edede9", (await WellAsync(host, "Custom color")).Attribute("value"));
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

		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Chore"), true);

		Assert.Equal(["category-editor", "pyx-chip", "pyx-chip-green"], (await EditorAsync(host)).Attribute("class")!.Split(' '));
		Assert.Equal("true", (await host.FindRadioAsync("Green for Chore")).Attribute("checked"));
		Assert.Null((await host.FindRadioAsync("Neutral for Chore")).Attribute("checked"));
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
		Assert.Equal(("Defect", CategoryColors.Pink), (stored.Name, stored.Color));
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
		await host.SetCheckedAsync(await host.FindRadioAsync("Pink for Chore"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Category stored = (await ReadAsync(database)).Single(category => category.Name == "Chore");
		Assert.Equal(CategoryColors.Pink, stored.Color);
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
		await host.SetCheckedAsync(await host.FindRadioAsync("Neutral for Feature"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.FeatureCategoryId);
		Assert.Equal(("Story", CategoryColors.Neutral), (stored.Name, stored.Color));
	}

	[Fact]
	public async Task Save_WithNothingChanged_WritesNothingAndClosesTheEditor()
	{
		await using TestDatabase database = new();
		GatedCategoryService categories = new(new CategoryService(database.ContextFactory));
		await using ServiceProvider services = GateRenderHost.CreateServices(database, categoryService: categories);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal((0, 0), (categories.Renames.Started, categories.Recolors.Started));
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
		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Bug"), true);

		await host.SubmitAsync(await EditorAsync(host));

		Assert.Equal(["A category named 'feature' already exists."], await host.AlertsAsync());
		Assert.Equal("feature", (await NameFieldAsync(host, "Bug")).Attribute("value"));
		Assert.Equal("true", (await host.FindRadioAsync("Green for Bug")).Attribute("checked"));
		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.BugCategoryId);
		Assert.Equal(("Bug", CategoryColors.Pink), (stored.Name, stored.Color));
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
		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Bug"), true);

		await host.ClickAsync(await host.FindButtonAsync("Cancel editing Bug"));

		GateRenderedElement chip = await host.FindButtonAsync("Edit Bug");
		Assert.Equal("Bug", chip.Text);
		Category stored = (await ReadAsync(database)).Single(category => category.Id == PyxieSeedData.BugCategoryId);
		Assert.Equal(("Bug", CategoryColors.Pink), (stored.Name, stored.Color));
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

	[Fact]
	public async Task Save_WritesTheColorItWasSubmittedWith_WhenAnotherIsChosenWhileTheRenameRuns()
	{
		await using TestDatabase database = new();
		GatedCategoryService categories = new(new CategoryService(database.ContextFactory));
		categories.Renames.Hold(1);
		await using ServiceProvider services = GateRenderHost.CreateServices(database, categoryService: categories);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "Defect");
		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Bug"), true);
		GateRenderedElement pink = await host.FindRadioAsync("Pink for Bug");
		await host.HoldAcknowledgementsAsync();

		Task save = host.SubmitAsync(await EditorAsync(host));
		await categories.Renames.WaitForStartAsync(1);

		// A choice the browser sent before it showed the editor busy arrives while the rename runs.
		await host.SetCheckedAsync(pink, true);
		categories.Renames.Release(1);
		await save;
		await host.AcknowledgeAsync();

		Category defect = Assert.Single(await categories.ListAsync(TestContext.Current.CancellationToken), category => category.Name == "Defect");
		Assert.Equal(CategoryColors.Green, defect.Color);
	}

	[Fact]
	public async Task Save_WritesTheColorItWasSubmittedWith_WhenAnotherIsPickedWhileTheRenameRuns()
	{
		await using TestDatabase database = new();
		GatedCategoryService categories = new(new CategoryService(database.ContextFactory));
		categories.Renames.Hold(1);
		await using ServiceProvider services = GateRenderHost.CreateServices(database, categoryService: categories);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(await NameFieldAsync(host, "Bug"), "Defect");
		await host.TypeAsync(await WellAsync(host, "Custom color for Bug"), "#1f3a93");
		GateRenderedElement well = await WellAsync(host, "Custom color for Bug");
		await host.HoldAcknowledgementsAsync();

		Task save = host.SubmitAsync(await EditorAsync(host));
		await categories.Renames.WaitForStartAsync(1);

		// A color the browser's picker moved to before the browser showed the editor busy arrives while the rename runs.
		await host.TypeAsync(well, "#e3f0ff");
		categories.Renames.Release(1);
		await save;
		await host.AcknowledgeAsync();

		Category defect = Assert.Single(await categories.ListAsync(TestContext.Current.CancellationToken), category => category.Name == "Defect");
		Assert.Equal(HexColor.Parse("#1F3A93"), defect.Color);
	}

	private static async Task<GateRenderedElement> WellAsync(GateInteractiveHost host, string name)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("type") == "color" && input.Attribute("aria-label") == name));
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
