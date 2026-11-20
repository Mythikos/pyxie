using System.Text;

using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

using static Pyxie.Tests.Web.SettingsTransferTestData;

namespace Pyxie.Tests.Web;

/// <summary>Export downloads the settings of the project the page shows. Import picks a file, which is read and previewed and opens the import dialog with what importing it would do, or with why it could not be read; a pick while a file is read or the dialog is open, and the empty change that resetting the file input raises, are ignored. An import that changed agents or categories tells the board its cards changed.</summary>
public sealed class SettingsTransferTests
{
	[Fact]
	public async Task Export_LinksToADownloadOfTheSettingsOfTheProjectThePageShows()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);

		GateRenderedElement export = Assert.Single(await host.FindElementsAsync("a", link => link.Text == "Export"));

		Assert.Equal($"settings/export?project={SettingsTransferHost.Project.Id}", export.Attribute("href"));
		Assert.True(export.Attributes.ContainsKey("download"), "The Export link should download the file rather than open it.");
	}

	[Fact]
	public async Task Import_OpensTheFilePickerOnceAndNothingElseUntilAFileIsPicked()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		FilePickerJsRuntime browser = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer, browser);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);

		await host.ClickAsync(await host.FindButtonAsync("Import"));

		Assert.Equal(1, browser.PickerRequests);
		Assert.False(await host.IsModalOpenAsync(), "Import only opens the browser's file picker; the dialog waits for a file.");
		Assert.Empty(await host.AlertsAsync());
		Assert.Equal(0, transfer.Previews.Started);
	}

	[Fact]
	public async Task Import_WhenTheBrowserRefusesToOpenTheFilePicker_LeavesThePageAsItWasAndTheNextPressAsksAgain()
	{
		await using TestDatabase database = new();
		FilePickerJsRuntime browser = new(refuses: true);
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);

		await host.ClickAsync(await host.FindButtonAsync("Import"));

		Assert.False(await host.IsModalOpenAsync(), "Blazorise swallows a refused picker, so the page stays as it was.");
		Assert.Empty(await host.AlertsAsync());
		await host.ClickAsync(await host.FindButtonAsync("Import"));
		Assert.Equal(2, browser.PickerRequests);
	}

	[Fact]
	public async Task Import_PressedWhileTheDialogIsOpen_OpensNoPicker()
	{
		await using TestDatabase database = new();
		FilePickerJsRuntime browser = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		await PickAsync(host, PickedSettingsFile.Of(FileAdding(TransferSection.Agents)));
		Assert.True(await host.IsModalOpenAsync(), "A picked file opens the dialog.");

		IReadOnlyList<GateRenderedElement> imports = await host.FindElementsAsync("button", button => button.Text == "Import");
		await host.ClickAsync(imports[0]);

		Assert.Equal(0, browser.PickerRequests);
	}

	[Fact]
	public async Task PickingASettingsFile_OpensTheDialogWithWhatImportingItWouldDo()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		PickedSettingsFile file = PickedSettingsFile.Of(new ProjectSettings(Agents: [Agent("Claude")], Gates: null, Categories: SeededCategoriesWithoutChore, Sandbox: null));

		await PickAsync(host, file);

		Assert.True(await host.IsModalOpenAsync(), "A picked settings file should open the import dialog.");
		Assert.Equal(["Agents", "Categories"], await SettingsImportDialogUnderTest.FindSectionsAsync(host));
		Assert.Equal(["Removes Chore"], await host.TextsWithClassAsync("import-removes"));
		Assert.Equal(1, file.Opens);
		Assert.Equal(1, transfer.Previews.Started);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not json")]
	[InlineData("""{ "name": "app" }""")]
	[InlineData("""{ "format": "pyxie.project-settings", "version": 2 }""")]
	public async Task PickingAFileThatIsNotASettingsFileThisPyxieReads_OpensTheDialogWithWhatTheReaderFoundAndCloseOnly(string content)
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		byte[] bytes = Encoding.UTF8.GetBytes(content);
		string expected = ResultAssert.Failed(SettingsFileReader.Read(bytes));

		await PickAsync(host, new PickedSettingsFile(bytes));

		Assert.True(await host.IsModalOpenAsync(), "A file that could not be read should open the dialog to say why.");
		Assert.Equal([expected], await host.AlertsAsync());
		Assert.Empty(await SettingsImportDialogUnderTest.FindSectionsAsync(host));
		Assert.Single(await host.FindElementsAsync("button", button => button.Text == "Close"));
		Assert.Empty(await host.FindElementsAsync("button", button => button.Text == "Cancel"));
		Assert.Equal(0, transfer.Previews.Started);
	}

	[Fact]
	public async Task PickingAFileReportedLargerThanTheLimit_OpensTheDialogWithTheSizeMessageWithoutReadingIt()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		PickedSettingsFile file = new(() => new MemoryStream(SettingsFileWriter.Write(FileAdding(TransferSection.Agents))), SettingsFileFormat.MaxBytes + 1);

		await PickAsync(host, file);

		Assert.Equal([SettingsFileFormat.TooLargeMessage], await host.AlertsAsync());
		Assert.Equal(0, file.Opens);
	}

	[Fact]
	public async Task AChangeWithNoFiles_IsIgnored()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);

		// Resetting the input after each read raises a change with no files.
		await PickAsync(host);

		Assert.False(await host.IsModalOpenAsync(), "A change with no files should open nothing.");
		Assert.Empty(await host.AlertsAsync());
		Assert.Equal(0, transfer.Previews.Started);
	}

	[Fact]
	public async Task PickingAFileWhileTheDialogIsOpen_IsIgnored()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		await PickAsync(host, PickedSettingsFile.Of(FileAdding(TransferSection.Agents)));
		PickedSettingsFile later = PickedSettingsFile.Of(FileAdding(TransferSection.Gates));

		await PickAsync(host, later);

		Assert.Equal(["Agents"], await SettingsImportDialogUnderTest.FindSectionsAsync(host));
		Assert.Equal(0, later.Opens);
		Assert.Equal(1, transfer.Previews.Started);
	}

	[Fact]
	public async Task PickingAFileWhileAnotherIsRead_IsIgnored()
	{
		await using TestDatabase database = new();
		GatedSettingsTransferService transfer = new(SettingsTransferHost.NewService(database));
		transfer.Previews.Hold(1);
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		PickedSettingsFile later = PickedSettingsFile.Of(FileAdding(TransferSection.Gates));

		Task first = PickAsync(host, PickedSettingsFile.Of(FileAdding(TransferSection.Agents)));
		await transfer.Previews.WaitForStartAsync(1);
		await PickAsync(host, later);
		transfer.Previews.Release(1);
		await first;

		Assert.Equal(["Agents"], await SettingsImportDialogUnderTest.FindSectionsAsync(host));
		Assert.Equal(0, later.Opens);
		Assert.Equal(1, transfer.Previews.Started);
	}

	[Fact]
	public async Task PickingAFileAfterTheDialogClosed_OpensItForThatFile()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await StartAsync(host);
		await PickAsync(host, PickedSettingsFile.Of(FileAdding(TransferSection.Agents)));
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await PickAsync(host, PickedSettingsFile.Of(FileAdding(TransferSection.Gates)));

		Assert.True(await host.IsModalOpenAsync(), "A file picked after the dialog closed should open it again.");
		Assert.Equal(["Gates"], await SettingsImportDialogUnderTest.FindSectionsAsync(host));
	}

	[Theory]
	[InlineData(TransferSection.Agents, true)]
	[InlineData(TransferSection.Categories, true)]
	[InlineData(TransferSection.Gates, false)]
	[InlineData(TransferSection.Sandbox, false)]
	public async Task Importing_ReportsTheSectionItChanged_AndTellsTheBoardOnlyWhenCardsShowIt(TransferSection section, bool tellsTheBoard)
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		int announcements = 0;
		services.GetRequiredService<PipelineActivity>().CardsChanged += () => announcements++;
		await using GateInteractiveHost host = new(services);
		List<IReadOnlySet<TransferSection>> imported = await StartAsync(host);
		await PickAsync(host, PickedSettingsFile.Of(FileAdding(section)));

		await host.ClickAsync(await SettingsImportDialogUnderTest.FindImportButtonAsync(host));

		Assert.Equal([section], Assert.Single(imported).Order());
		Assert.Equal(tellsTheBoard ? 1 : 0, announcements);
		Assert.False(await host.IsModalOpenAsync(), "The import should have closed the dialog.");
		Assert.Empty(await host.AlertsAsync());
	}

	// Renders the controls for the stand-in project and returns the sections each import they report changed.
	private static async Task<List<IReadOnlySet<TransferSection>>> StartAsync(GateInteractiveHost host)
	{
		List<IReadOnlySet<TransferSection>> imported = [];
		await host.StartAsync<SettingsTransfer>(new Dictionary<string, object?>
		{
			[nameof(SettingsTransfer.Project)] = SettingsTransferHost.Project,
			[nameof(SettingsTransfer.Imported)] = EventCallback.Factory.Create<IReadOnlySet<TransferSection>>(new object(), imported.Add),
		});

		return imported;
	}

	// The browser reports a pick to the file input's script, which tells the input, which raises Changed with the files.
	private static Task PickAsync(GateInteractiveHost host, params IFileEntry[] files)
	{
		return host.RaiseComponentEventAsync<FileInput, FileChangedEventArgs>(input => input.Changed, new FileChangedEventArgs(files));
	}
}
