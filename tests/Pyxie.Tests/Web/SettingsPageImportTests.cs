using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Pages;
using Pyxie.Web.Components.Settings;

using Xunit;

using static Pyxie.Tests.Web.SettingsTransferTestData;

namespace Pyxie.Tests.Web;

/// <summary>A panel of the Settings page reads its settings when it is made, so after an import the page makes anew the panel of each section the import changed, which then shows the imported settings, and keeps the other panels as they are, with whatever the person was editing in them.</summary>
public sealed class SettingsPageImportTests
{
	private const string UnsavedDockerfile = "FROM ${PYXIE_BASE}\nRUN echo unsaved\n";
	private const string ImportedDockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo imported\n";

	[Theory]
	[InlineData(TransferSection.Agents)]
	[InlineData(TransferSection.Gates)]
	[InlineData(TransferSection.Categories)]
	[InlineData(TransferSection.Sandbox)]
	public async Task Import_ThatChangedOneSection_MakesThatPanelAnewAndKeepsTheOthersAsTheyWere(TransferSection changed)
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost page = new(services);
		await page.StartAsync<Settings>();
		await page.TypeAsync(await DockerfileFieldAsync(page), UnsavedDockerfile);
		Dictionary<TransferSection, IComponent> before = await PanelsAsync(page);
		ProjectSettings exported = ResultAssert.Succeeded(await SettingsTransferHost.NewService(database).ExportAsync(SettingsTransferHost.Project.Id, TestContext.Current.CancellationToken));
		ProjectSettings file = changed switch
		{
			TransferSection.Agents => exported with { Agents = [Agent("Claude")] },
			TransferSection.Gates => exported with { Gates = [Gate("Lint")] },
			TransferSection.Categories => exported with { Categories = [.. exported.Categories ?? [], new CategoryInput("Spike", HexColor.Parse("#3A6EA5"))] },
			_ => exported with { Sandbox = new SandboxSettingsInput(ImportedDockerfile) },
		};
		await page.RaiseComponentEventAsync<FileInput, FileChangedEventArgs>(input => input.Changed, new FileChangedEventArgs([new PickedSettingsFile(SettingsFileWriter.Write(file))]));

		await page.ClickAsync(await SettingsImportDialogUnderTest.FindImportButtonAsync(page));

		Dictionary<TransferSection, IComponent> after = await PanelsAsync(page);
		foreach (TransferSection section in Enum.GetValues<TransferSection>())
		{
			bool kept = ReferenceEquals(before[section], after[section]);
			Assert.True(kept == (section != changed), $"The {section} panel should {(section == changed ? "be made anew, since the import changed its settings" : "be kept, since the import did not change its settings")}.");
		}

		string expectedText = changed switch
		{
			TransferSection.Agents => "Claude",
			TransferSection.Gates => "Lint",
			TransferSection.Categories => "Spike",
			_ => ImportedDockerfile,
		};
		Assert.Contains(expectedText, changed == TransferSection.Sandbox ? (await DockerfileFieldAsync(page)).Attribute("value") ?? string.Empty : await PanelTextAsync(page, changed), StringComparison.Ordinal);
		if (changed != TransferSection.Sandbox)
		{
			Assert.Equal(UnsavedDockerfile, (await DockerfileFieldAsync(page)).Attribute("value"));
		}
	}

	[Fact]
	public async Task ExternalDockerfileChangePreservesAnUnsavedDraftUntilReload()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost page = new(services);
		await page.StartAsync<Settings>();
		await page.TypeAsync(await DockerfileFieldAsync(page), UnsavedDockerfile);
		ResultAssert.Succeeded(await services.GetRequiredService<ISandboxSettingsService>().SaveAsync(new SandboxSettingsInput(ImportedDockerfile), 0, TestContext.Current.CancellationToken));
		services.GetRequiredService<PipelineActivity>().NotifySettingsChanged(SettingsTransferHost.Project.Id, TransferSection.Sandbox);

		Assert.Equal(UnsavedDockerfile, (await DockerfileFieldAsync(page)).Attribute("value"));
		GateRenderedElement reload = Assert.Single(await page.FindElementsAsync("button", button => button.Text == "Reload Sandbox"));
		await page.ClickAsync(reload);
		Assert.Equal(ImportedDockerfile, (await DockerfileFieldAsync(page)).Attribute("value"));
	}

	[Fact]
	public async Task ADelayedReloadCannotDiscardTheNextDraft()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost page = new(services);
		await page.StartAsync<Settings>();
		PipelineActivity activity = services.GetRequiredService<PipelineActivity>();
		await page.TypeAsync(await DockerfileFieldAsync(page), UnsavedDockerfile);
		activity.NotifySettingsChanged(SettingsTransferHost.Project.Id, TransferSection.Sandbox);
		GateRenderedElement firstReload = await page.FindButtonAsync("Reload Sandbox");
		await page.HoldAcknowledgementsAsync();
		try
		{
			await page.ClickAsync(firstReload);
			await page.TypeAsync(await DockerfileFieldAsync(page), ImportedDockerfile);
			activity.NotifySettingsChanged(SettingsTransferHost.Project.Id, TransferSection.Sandbox);
			await page.ClickAsync(firstReload);
			Assert.Equal(ImportedDockerfile, (await DockerfileFieldAsync(page)).Attribute("value"));
			Assert.Single(await page.FindElementsAsync("button", button => button.Text == "Reload Sandbox"));
		}
		finally
		{
			await page.AcknowledgeAsync();
		}
	}

	[Fact]
	public async Task ASaveFromThePreviousProjectIsRejectedBeforeItsPanelIsReplaced()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = SettingsTransferHost.CreateServices(database);
		await using GateInteractiveHost page = new(services);
		await page.StartAsync<Settings>();
		await page.TypeAsync(await DockerfileFieldAsync(page), UnsavedDockerfile);
		GateRenderedElement save = Assert.Single(await page.FindElementsAsync("button", button => button.Attribute("class")?.Contains("sandbox-save", StringComparison.Ordinal) == true));
		services.GetRequiredService<OpenProject>().Set(new Project { Id = SettingsTransferHost.Project.Id + 1, Name = "Other", Folder = "other" });
		await page.ClickAsync(save);
		Assert.Equal(0, (await services.GetRequiredService<ISandboxSettingsService>().GetAsync(TestContext.Current.CancellationToken)).Version);
		Assert.Contains(await page.AlertsAsync(), alert => alert.Contains("project", StringComparison.OrdinalIgnoreCase));
	}

	private static async Task<Dictionary<TransferSection, IComponent>> PanelsAsync(GateInteractiveHost page)
	{
		return new Dictionary<TransferSection, IComponent>
		{
			[TransferSection.Agents] = Assert.Single(await page.FindComponentsAsync<AgentTable>()),
			[TransferSection.Gates] = Assert.Single(await page.FindComponentsAsync<GateTable>()),
			[TransferSection.Categories] = Assert.Single(await page.FindComponentsAsync<CategoryList>()),
			[TransferSection.Sandbox] = Assert.Single(await page.FindComponentsAsync<SandboxSettingsEditor>()),
		};
	}

	private static async Task<string> PanelTextAsync(GateInteractiveHost page, TransferSection section)
	{
		string id = SettingsSections.Of(section).Id;
		IReadOnlyList<GateRenderedElement> panels = await page.FindElementsAsync("section", panel => panel.Attribute("id") == id);

		return Assert.Single(panels).Text;
	}

	private static async Task<GateRenderedElement> DockerfileFieldAsync(GateInteractiveHost page)
	{
		return Assert.Single(await page.FindElementsAsync("textarea", field => field.Attribute("id") == "sandbox-dockerfile"));
	}
}
