using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

using static Pyxie.Tests.Web.SettingsTransferTestData;

namespace Pyxie.Tests.Web;

/// <summary>The import dialog offers each section a settings file holds, all chosen at first and named as the Settings page names them, with what importing each would remove or why it cannot be imported. Import replaces the chosen sections and closes the dialog; a refused import keeps it open with the reasons and what the import would do now; a file that could not be read shows why with Close only.</summary>
public sealed class SettingsImportDialogTests
{
	[Fact]
	public async Task Dialog_OffersOnlyTheSectionsTheFileHolds_AllChosen()
	{
		await using TestDatabase database = new();
		ProjectSettings settings = new(Agents: [Agent("Claude")], Gates: null, Categories: SeededCategoriesWithoutChore, Sandbox: null);

		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);

		Assert.Equal(["Agents", "Categories"], await dialog.SectionsAsync());
		Assert.True(await dialog.IsPickedAsync(TransferSection.Agents), "Every section the file holds should start chosen.");
		Assert.True(await dialog.IsPickedAsync(TransferSection.Categories), "Every section the file holds should start chosen.");
		Assert.True(await dialog.CanImportAsync(), "A file whose sections can all be imported should be ready to import.");
		Assert.True(await dialog.Host.IsModalOpenAsync(), "The dialog should be shown.");
	}

	[Theory]
	[InlineData(TransferSection.Agents, "Agents")]
	[InlineData(TransferSection.Gates, "Gates")]
	[InlineData(TransferSection.Categories, "Categories")]
	[InlineData(TransferSection.Sandbox, "Sandbox")]
	public async Task Dialog_NamesASectionAsTheSettingsPageDoes(TransferSection section, string expectedTitle)
	{
		await using TestDatabase database = new();

		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(section));

		Assert.Equal([expectedTitle], await dialog.SectionsAsync());
	}

	[Fact]
	public async Task Dialog_SaysWhatEachSectionRemoves_AndNothingForASectionThatRemovesNothing()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Old", canWork: true, canReview: false);
		await TestData.AddAgentAsync(database, "Older", canWork: true, canReview: false);
		ProjectSettings settings = new(Agents: [], Gates: [Gate("Build")], Categories: SeededCategoriesWithoutChore, Sandbox: null);

		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);

		Assert.Equal(["Removes Old, Older", "Removes Chore"], await dialog.RemovesAsync());
		Assert.Empty(await dialog.ProblemsAsync());
	}

	[Fact]
	public async Task Dialog_WithASectionThatBreaksARule_ShowsItsProblemAndWaitsUntilItIsLeftOut()
	{
		await using TestDatabase database = new();
		ProjectSettings settings = new(Agents: [Agent("Claude")], Gates: [Gate("Build"), Gate("build")], Categories: null, Sandbox: null);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);

		Assert.Equal(["The file has more than one gate named 'Build'."], await dialog.ProblemsAsync());
		Assert.True(await dialog.IsPickedAsync(TransferSection.Gates), "A blocked section should start chosen too, so the person decides to leave it out.");
		Assert.False(await dialog.CanImportAsync(), "Import should wait while a chosen section cannot be imported.");

		await dialog.SetPickedAsync(TransferSection.Gates, false);

		Assert.False(await dialog.IsPickedAsync(TransferSection.Gates), "Clearing the choice should leave the section out.");
		Assert.True(await dialog.CanImportAsync(), "Import should be ready once the blocked section is left out.");
	}

	[Fact]
	public async Task Dialog_WithAnAgentAnUnfinishedCardNeeds_ShowsTheAgentEditorsRefusalAndWaits()
	{
		await using TestDatabase database = new();
		int old = (await TestData.AddAgentAsync(database, "Old", canWork: true, canReview: false)).Id;
		await TestData.AddCardAsync(database, "Login", CardLane.Queue, workerAgentId: old);
		ProjectSettings settings = new(Agents: [Agent("Claude")], Gates: null, Categories: null, Sandbox: null);

		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);

		Assert.Equal(["Agent 'Old' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted."], await dialog.ProblemsAsync());
		Assert.Empty(await dialog.RemovesAsync());
		Assert.False(await dialog.CanImportAsync(), "Import should wait while the only chosen section cannot be imported.");
	}

	[Fact]
	public async Task Dialog_WithEverySectionLeftOut_WaitsForAChoice()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents));

		await dialog.SetPickedAsync(TransferSection.Agents, false);

		Assert.False(await dialog.CanImportAsync(), "Import should wait until a section is chosen.");
	}

	[Fact]
	public async Task Import_ReplacesOnlyTheChosenSections_ReportsTheOnesItChangedAndAsksToClose()
	{
		await using TestDatabase database = new();
		ProjectSettings settings = new(Agents: [Agent("Claude")], Gates: [], Categories: SeededCategoriesWithoutChore, Sandbox: null);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);
		await dialog.SetPickedAsync(TransferSection.Categories, false);

		await dialog.PressImportAsync();

		Assert.Equal(["Claude"], await ReadAgentNamesAsync(database));
		Assert.Contains("Chore", await ReadCategoryNamesAsync(database));
		IReadOnlySet<TransferSection> changed = Assert.Single(dialog.Imported);
		Assert.Equal([TransferSection.Agents], changed.Order());
		Assert.Equal([false], dialog.Reported);
		Assert.False(await dialog.Host.IsModalOpenAsync(), "An import should close the dialog.");
		Assert.Empty(await dialog.Host.AlertsAsync());
	}

	[Fact]
	public async Task Import_RefusedBecauseTheProjectChangedAfterThePreview_SaysSoStaysOpenAndShowsWhatTheImportWouldDoNow()
	{
		await using TestDatabase database = new();
		ProjectSettings settings = new(Agents: null, Gates: null, Categories: SeededCategoriesWithoutChore, Sandbox: null);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);
		await AddCategoryAsync(database, "Docs");

		await dialog.PressImportAsync();

		Assert.Equal([SettingsImportDialogUnderTest.StaleMessage], await dialog.Host.AlertsAsync());
		Assert.Equal(["Removes Chore, Docs"], await dialog.RemovesAsync());
		Assert.Equal(["Bug", "Chore", "Docs", "Feature"], (await ReadCategoryNamesAsync(database)).Order());
		Assert.Empty(dialog.Imported);
		Assert.Empty(dialog.Reported);
		Assert.True(await dialog.Host.IsModalOpenAsync(), "A refused import should keep the dialog open.");
		Assert.True(await dialog.IsPickedAsync(TransferSection.Categories), "The choices should stay as they were.");
		Assert.True(await dialog.CanImportAsync(), "The refreshed preview can be imported, so Import should be ready again.");
	}

	[Fact]
	public async Task Import_OfAnAlreadyChangedDockerfileKeepsANewerEditUntilThePreviewIsReviewedAgain()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Sandbox));
		SandboxSettingsService sandbox = new(database.ContextFactory, database.Clock);
		const string replacement = "FROM ${PYXIE_BASE}\nRUN echo external\n";
		ResultAssert.Succeeded(await sandbox.SaveAsync(new SandboxSettingsInput(replacement), 0, TestContext.Current.CancellationToken));

		await dialog.PressImportAsync();

		Assert.Equal([SettingsImportDialogUnderTest.StaleMessage], await dialog.Host.AlertsAsync());
		Assert.Equal(replacement, (await sandbox.GetAsync(TestContext.Current.CancellationToken)).Dockerfile);
		Assert.True(await dialog.Host.IsModalOpenAsync(), "A stale preview stays open for review.");
	}

	[Fact]
	public async Task Import_AfterARefusalRefreshedThePreview_ImportsWhatTheDialogNowShows()
	{
		await using TestDatabase database = new();
		ProjectSettings settings = new(Agents: null, Gates: null, Categories: SeededCategoriesWithoutChore, Sandbox: null);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);
		await AddCategoryAsync(database, "Docs");
		await dialog.PressImportAsync();

		await dialog.PressImportAsync();

		Assert.Equal(["Bug", "Feature"], (await ReadCategoryNamesAsync(database)).Order());
		Assert.Equal([TransferSection.Categories], Assert.Single(dialog.Imported).Order());
		Assert.Equal([false], dialog.Reported);
		Assert.Empty(await dialog.Host.AlertsAsync());
	}

	[Fact]
	public async Task Import_RefusedByACardThatNeedsAnAgentTheFileRemoves_ListsTheRefusalAndWaitsUntilTheSectionIsLeftOut()
	{
		await using TestDatabase database = new();
		int old = (await TestData.AddAgentAsync(database, "Old", canWork: true, canReview: false)).Id;
		ProjectSettings settings = new(Agents: [Agent("Claude")], Gates: null, Categories: SeededCategoriesWithDocs, Sandbox: null);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, settings);
		await TestData.AddCardAsync(database, "Login", CardLane.Queue, workerAgentId: old);
		const string Refusal = "Agent 'Old' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.";

		await dialog.PressImportAsync();

		Assert.Equal([$"Nothing was imported.\n{Refusal}"], await dialog.Host.AlertsAsync());
		Assert.Equal([Refusal], await dialog.ProblemsAsync());
		Assert.False(await dialog.CanImportAsync(), "Import should wait while the chosen agents section is now blocked.");
		Assert.Equal(["Old"], await ReadAgentNamesAsync(database));
		Assert.True(await dialog.Host.IsModalOpenAsync(), "A refused import should keep the dialog open.");

		await dialog.SetPickedAsync(TransferSection.Agents, false);
		await dialog.PressImportAsync();

		Assert.Equal(["Old"], await ReadAgentNamesAsync(database));
		Assert.Contains("Docs", await ReadCategoryNamesAsync(database));
		Assert.Equal([TransferSection.Categories], Assert.Single(dialog.Imported).Order());
		Assert.Equal([false], dialog.Reported);
	}

	[Fact]
	public async Task Import_WhenAnotherProjectWasOpenedAfterThePreview_SaysSoAndLeavesNothingToImport()
	{
		await using TestDatabase database = new();
		OpenProject openProject = StandInProject.Open();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents), SettingsTransferHost.NewService(database, openProject));
		openProject.Set(new Project { Id = SettingsTransferHost.Project.Id + 1, Name = "other", Folder = Path.Combine(Path.GetTempPath(), "pyxie-other-project"), IsOpen = true });

		await dialog.PressImportAsync();

		Assert.Equal(["Another project is open, so nothing was imported.\nThe project is not open."], await dialog.Host.AlertsAsync());
		Assert.Empty(await dialog.SectionsAsync());
		Assert.False(await dialog.CanImportAsync(), "With no preview of the open project there is nothing to import.");
		Assert.Empty(await ReadAgentNamesAsync(database));
		Assert.Empty(dialog.Reported);
	}

	[Fact]
	public async Task Dialog_ForAFileThatCouldNotBeRead_ShowsWhyWithCloseOnly()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, ServiceResult<SettingsImportOpening>.Failure(SettingsFileReader.NotSettingsMessage));

		Assert.Equal([SettingsFileReader.NotSettingsMessage], await dialog.Host.AlertsAsync());
		Assert.Equal(["Close"], (await dialog.Host.FindElementsAsync("button")).Select(button => button.Text));
		Assert.Empty(await dialog.SectionsAsync());

		await dialog.Host.ClickAsync(await dialog.Host.FindButtonAsync("Close"));

		Assert.Equal([false], dialog.Reported);
		Assert.False(await dialog.Host.IsModalOpenAsync(), "Close should close the dialog.");
	}

	[Fact]
	public async Task Cancel_ClosesTheDialogWithoutImporting()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents));

		await dialog.Host.ClickAsync(await dialog.Host.FindButtonAsync("Cancel"));

		Assert.Equal([false], dialog.Reported);
		Assert.Empty(dialog.Imported);
		Assert.Empty(await ReadAgentNamesAsync(database));
		Assert.False(await dialog.Host.IsModalOpenAsync(), "Cancel should close the dialog.");
	}
}
