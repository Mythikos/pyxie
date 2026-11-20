using Pyxie.Data.Entities;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An import of the sandbox settings writes the Dockerfile only when it differs from the one in effect (the saved one, or the default while none was saved), and then advances the version, so an editor that read the settings before cannot save over the import, while one that read them before an import that changed nothing still can.</summary>
public sealed class SettingsTransferServiceSandboxTests
{
	private const string ChangedElsewhereMessage = "The sandbox settings were changed elsewhere. Reload them, then save again.";
	private const string StaleMessage = "The project's settings changed after the file was read, so nothing was imported. Check what the import will do, then import again.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static ProjectSettings File(string dockerfile)
	{
		return new ProjectSettings(null, null, null, new SandboxSettingsInput(dockerfile));
	}

	private static async Task<SandboxSettings> SaveAsync(TestDatabase database, string dockerfile)
	{
		return ResultAssert.Succeeded(await new SandboxSettingsService(database.ContextFactory, database.Clock).SaveAsync(new SandboxSettingsInput(dockerfile), expectedVersion: 0, Token));
	}

	public static TheoryData<string> DefaultAsWritten()
	{
		return new TheoryData<string>(
			SandboxSettingsInputRules.NormalizeDockerfile(SandboxDefaults.Dockerfile),
			SandboxDefaults.Dockerfile.ReplaceLineEndings("\r\n") + "\r\n\r\n",
			"\n  \n" + SandboxDefaults.Dockerfile.TrimEnd() + "   ");
	}

	[Theory]
	[MemberData(nameof(DefaultAsWritten))]
	public async Task ImportAsync_WhenNoneWereSavedAndTheFileHoldsTheDefault_SavesNothing(string dockerfile)
	{
		await using TestDatabase database = new();
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, File(dockerfile));
		ResultAssert.Succeeded(await service.ImportAsync(File(dockerfile), preview, SettingsTransferServiceTestData.Only(TransferSection.Sandbox), Token));

		Assert.False(preview.Find(TransferSection.Sandbox)?.Changes.HasChanges ?? true, "The default Dockerfile, however its lines end, is the one in effect while none was saved.");
		Assert.Empty(await SandboxSettingsRows.ReadAsync(database));
	}

	[Fact]
	public async Task ImportAsync_WhenNoneWereSavedAndTheFileDiffers_SavesTheFirstRowAtVersionOne()
	{
		await using TestDatabase database = new();
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		DateTime importedAt = database.Clock.Tick();

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, File("FROM ${PYXIE_BASE}\r\nRUN make\r\n"));
		ResultAssert.Succeeded(await service.ImportAsync(File("FROM ${PYXIE_BASE}\r\nRUN make\r\n"), preview, SettingsTransferServiceTestData.Only(TransferSection.Sandbox), Token));

		Assert.Equal(["Dockerfile"], preview.Find(TransferSection.Sandbox)?.Changes.Changed ?? []);
		SandboxSettings row = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(("FROM ${PYXIE_BASE}\nRUN make\n", 1, importedAt), (row.Dockerfile, row.Version, row.UpdatedUtc));
	}

	[Fact]
	public async Task ImportAsync_WhenSavedAndTheFileDiffers_WritesItAdvancesTheVersionAndStampsTheImport()
	{
		await using TestDatabase database = new();
		SandboxSettings saved = await SaveAsync(database, SettingsTransferServiceTestData.Dockerfile("RUN echo saved"));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		DateTime importedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, File(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")), SettingsTransferServiceTestData.Only(TransferSection.Sandbox)));

		SandboxSettings row = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal((saved.Id, SettingsTransferServiceTestData.Dockerfile("RUN echo imported"), saved.Version + 1, importedAt), (row.Id, row.Dockerfile, row.Version, row.UpdatedUtc));
	}

	[Fact]
	public async Task ImportAsync_WhenSavedAndTheFileHoldsTheDefault_WritesTheDefault()
	{
		await using TestDatabase database = new();
		SandboxSettings saved = await SaveAsync(database, SettingsTransferServiceTestData.Dockerfile("RUN echo saved"));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		DateTime importedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, File(SandboxDefaults.Dockerfile), SettingsTransferServiceTestData.Only(TransferSection.Sandbox)));

		SandboxSettings row = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal((SettingsTransferServiceTestData.DefaultDockerfile, saved.Version + 1, importedAt), (row.Dockerfile, row.Version, row.UpdatedUtc));
	}

	[Theory]
	[InlineData("ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo saved\n")]
	[InlineData("ARG PYXIE_BASE\r\nFROM ${PYXIE_BASE}\r\nRUN echo saved\r\n\r\n")]
	[InlineData("  ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo saved")]
	public async Task ImportAsync_WhenSavedAndTheFileHoldsTheSameDockerfile_LeavesTheRowAlone(string dockerfile)
	{
		await using TestDatabase database = new();
		SandboxSettings saved = await SaveAsync(database, SettingsTransferServiceTestData.Dockerfile("RUN echo saved"));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		database.Clock.Tick();

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, File(dockerfile));
		ResultAssert.Succeeded(await service.ImportAsync(File(dockerfile), preview, SettingsTransferServiceTestData.Only(TransferSection.Sandbox), Token));

		Assert.False(preview.Find(TransferSection.Sandbox)?.Changes.HasChanges ?? true, "The same Dockerfile, however its lines end, is no change.");
		SandboxSettings row = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal((saved.Id, saved.Dockerfile, saved.Version, saved.UpdatedUtc), (row.Id, row.Dockerfile, row.Version, row.UpdatedUtc));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SaveAsync_WithTheVersionReadBeforeAnImportThatChangedTheDockerfile_IsRefusedAsChangedElsewhere(bool savedBefore)
	{
		await using TestDatabase database = new();
		if (savedBefore)
		{
			await SaveAsync(database, SettingsTransferServiceTestData.Dockerfile("RUN echo saved"));
		}

		SandboxSettingsService editor = new(database.ContextFactory, database.Clock);
		SandboxSettings read = await editor.GetAsync(Token);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, File(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")), SettingsTransferServiceTestData.Only(TransferSection.Sandbox)));

		string error = ResultAssert.Failed(await editor.SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo edited")), read.Version, Token));

		Assert.Equal(ChangedElsewhereMessage, error);
		Assert.Equal(SettingsTransferServiceTestData.Dockerfile("RUN echo imported"), Assert.Single(await SandboxSettingsRows.ReadAsync(database)).Dockerfile);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SaveAsync_WithTheVersionReadBeforeAnImportThatKeptTheDockerfile_Saves(bool savedBefore)
	{
		await using TestDatabase database = new();
		string current = savedBefore ? SettingsTransferServiceTestData.Dockerfile("RUN echo saved") : SettingsTransferServiceTestData.DefaultDockerfile;
		if (savedBefore)
		{
			await SaveAsync(database, current);
		}

		SandboxSettingsService editor = new(database.ContextFactory, database.Clock);
		SandboxSettings read = await editor.GetAsync(Token);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, File(current), SettingsTransferServiceTestData.Only(TransferSection.Sandbox)));

		SandboxSettings edited = ResultAssert.Succeeded(await editor.SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo edited")), read.Version, Token));

		Assert.Equal(SettingsTransferServiceTestData.Dockerfile("RUN echo edited"), edited.Dockerfile);
	}

	// The import writes the row only while it still has the version the import read; a save that lands while the import writes its other sections makes it roll every section back.
	[Fact]
	public async Task ImportAsync_WhenTheSandboxSettingsAreSavedWhileItWrites_ImportsNothing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await SaveAsync(database, SettingsTransferServiceTestData.Dockerfile("RUN echo saved"));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new([SettingsTransferServiceTestData.Agent("Agent"), SettingsTransferServiceTestData.Agent("Newcomer")], null, null, new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();
		interceptor.Arm("UPDATE \"SandboxSettings\" SET \"Version\" = \"Version\" + 1");

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Agents, TransferSection.Sandbox), Token));

		interceptor.AssertFired();
		Assert.Equal(StaleMessage, error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}
}
