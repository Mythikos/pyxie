using Blazorise;

using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

using static Pyxie.Tests.Web.SettingsTransferTestData;

namespace Pyxie.Tests.Web;

/// <summary>The import dialog imports once per opening, and only what it showed when Import was pressed. Presses the person made before the browser showed the result of an earlier one still reach the server: a second Import while the first runs, or after the dialog closed, a press drawn with the preview a refusal has since replaced, and the second click of a double click must import nothing. Its modal reports VisibleChanged 150 ms late, after its animation, so the dialog takes a report only as a close the person made while it is open and nothing runs.</summary>
public sealed class SettingsImportDialogDoubleSubmitTests
{
	[Fact]
	public async Task Import_PressedAgainWhileTheFirstImportRuns_ImportsOnce()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		transfer.Imports.Hold(1);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents), transfer);
		GateRenderedElement import = await dialog.ImportButtonAsync();

		Task first = dialog.Host.ClickAsync(import);
		await transfer.Imports.WaitForStartAsync(1);
		await dialog.Host.ClickAsync(import);
		transfer.Imports.Release(1);
		await first;

		Assert.Equal(1, transfer.Imports.Started);
		Assert.Equal(["Claude"], await ReadAgentNamesAsync(database));
		Assert.Single(dialog.Imported);
		Assert.Equal([false], dialog.Reported);
		Assert.Empty(await dialog.Host.AlertsAsync());
	}

	[Fact]
	public async Task Import_PressedAgainAfterTheDialogClosed_ImportsNothingMore()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents), transfer);
		GateRenderedElement import = await dialog.ImportButtonAsync();
		await dialog.Host.HoldAcknowledgementsAsync();

		await dialog.Host.ClickAsync(import);
		await dialog.Host.ClickAsync(import);
		await dialog.Host.AcknowledgeAsync();

		Assert.Equal(1, transfer.Imports.Started);
		Assert.Single(dialog.Imported);
		Assert.Equal([false], dialog.Reported);
		Assert.Empty(await dialog.Host.AlertsAsync());
		Assert.False(await dialog.Host.IsModalOpenAsync(), "The import should have closed the dialog.");
	}

	[Fact]
	public async Task Import_PressedWithThePreviewARefusalReplaced_ImportsNothing()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, StaleCategoriesFile(), transfer);
		await AddCategoryAsync(database, "Docs");
		GateRenderedElement import = await dialog.ImportButtonAsync();
		await dialog.Host.HoldAcknowledgementsAsync();

		// The second press left the browser before it showed the refusal and the refreshed preview, which would also remove Docs.
		await dialog.Host.ClickAsync(import);
		await dialog.Host.ClickAsync(import);
		await dialog.Host.AcknowledgeAsync();

		Assert.Equal(1, transfer.Imports.Started);
		Assert.Equal(["Bug", "Chore", "Docs", "Feature"], (await ReadCategoryNamesAsync(database)).Order());
		Assert.Equal([SettingsImportDialogUnderTest.StaleMessage], await dialog.Host.AlertsAsync());
		Assert.Equal(["Removes Chore, Docs"], await dialog.RemovesAsync());
		Assert.Empty(dialog.Reported);

		await dialog.PressImportAsync();

		Assert.Equal(2, transfer.Imports.Started);
		Assert.Equal(["Bug", "Feature"], (await ReadCategoryNamesAsync(database)).Order());
		Assert.Equal([false], dialog.Reported);
	}

	[Fact]
	public async Task Import_DoubleClicked_ImportsNothingOnTheSecondClick()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, StaleCategoriesFile(), transfer);
		await AddCategoryAsync(database, "Docs");
		await dialog.PressImportAsync();

		// The first click was refused and the browser showed the refreshed preview before the second click of the double click landed on Import.
		await dialog.PressImportAsync(clicks: 2);

		Assert.Equal(1, transfer.Imports.Started);
		Assert.Equal(["Bug", "Chore", "Docs", "Feature"], (await ReadCategoryNamesAsync(database)).Order());
		Assert.Empty(dialog.Reported);
		Assert.True(await dialog.CanImportAsync(), "The refreshed preview can be imported, so only the double click should have been ignored.");
	}

	[Fact]
	public async Task Choice_ChangedWhileAnImportRuns_IsIgnored()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		transfer.Imports.Hold(1);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, StaleCategoriesFile(), transfer);
		await AddCategoryAsync(database, "Docs");
		GateRenderedElement categories = await dialog.ChoiceAsync(TransferSection.Categories);

		Task import = dialog.PressImportAsync();
		await transfer.Imports.WaitForStartAsync(1);
		await dialog.Host.SetCheckedAsync(categories, false);
		transfer.Imports.Release(1);
		await import;

		Assert.Equal([SettingsImportDialogUnderTest.StaleMessage], await dialog.Host.AlertsAsync());
		Assert.True(await dialog.IsPickedAsync(TransferSection.Categories), "A choice changed while the import ran should have been ignored.");
	}

	[Fact]
	public async Task Cancel_PressedAgainAfterTheDialogClosed_ReportsNothingMore()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents));
		GateRenderedElement cancel = await dialog.Host.FindButtonAsync("Cancel");
		await dialog.Host.HoldAcknowledgementsAsync();

		await dialog.Host.ClickAsync(cancel);
		await dialog.Host.ClickAsync(cancel);
		await dialog.Host.AcknowledgeAsync();

		Assert.Equal([false], dialog.Reported);
	}

	[Fact]
	public async Task ModalsReportThatItOpened_IsNotPassedOn()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents));

		await dialog.Host.RaiseComponentEventAsync<Modal, bool>(modal => modal.VisibleChanged, true);

		Assert.Empty(dialog.Reported);
		Assert.True(await dialog.Host.IsModalOpenAsync(), "The dialog should still be shown.");
	}

	[Fact]
	public async Task ModalsReportThatThePersonClosedIt_IsPassedOnAndLeavesNothingToImport()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents), transfer);
		GateRenderedElement import = await dialog.ImportButtonAsync();
		await dialog.Host.HoldAcknowledgementsAsync();

		// The person pressed Escape, and pressed Import before the browser hid the dialog.
		await dialog.Host.RaiseComponentEventAsync<Modal, bool>(modal => modal.VisibleChanged, false);
		await dialog.Host.ClickAsync(import);
		await dialog.Host.AcknowledgeAsync();

		Assert.Equal([false], dialog.Reported);
		Assert.Equal(0, transfer.Imports.Started);
		Assert.Empty(await ReadAgentNamesAsync(database));
		Assert.False(await dialog.Host.IsModalOpenAsync(), "The close should have hidden the dialog.");
	}

	[Fact]
	public async Task ModalsLateReportOfTheCloseAnImportMade_IsNotPassedOnAgain()
	{
		await using TestDatabase database = new();
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, FileAdding(TransferSection.Agents));
		await dialog.PressImportAsync();

		await dialog.Host.RaiseComponentEventAsync<Modal, bool>(modal => modal.VisibleChanged, false);

		Assert.Equal([false], dialog.Reported);
		Assert.Single(dialog.Imported);
	}

	[Fact]
	public async Task ModalsReportOfACloseWhileAnImportRuns_IsNotPassedOn()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		transfer.Imports.Hold(1);
		await using SettingsImportDialogUnderTest dialog = await SettingsImportDialogUnderTest.OpenAsync(database, StaleCategoriesFile(), transfer);
		await AddCategoryAsync(database, "Docs");

		Task import = dialog.PressImportAsync();
		await transfer.Imports.WaitForStartAsync(1);
		await dialog.Host.RaiseComponentEventAsync<Modal, bool>(modal => modal.VisibleChanged, false);
		transfer.Imports.Release(1);
		await import;

		Assert.Empty(dialog.Reported);
		Assert.Equal([SettingsImportDialogUnderTest.StaleMessage], await dialog.Host.AlertsAsync());
		Assert.True(await dialog.Host.IsModalOpenAsync(), "A close reported while the import ran should not have closed the dialog.");
	}

	// A file whose import is refused once the test adds Docs after the preview, and whose refreshed preview, which also removes Docs, can be imported.
	private static ProjectSettings StaleCategoriesFile()
	{
		return new ProjectSettings(Agents: null, Gates: null, Categories: SeededCategoriesWithoutChore, Sandbox: null);
	}
}
