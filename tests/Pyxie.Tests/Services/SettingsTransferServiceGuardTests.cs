using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An import acts only on what was picked and reviewed: some section must be picked and the file must hold it; the project must still be the open one; and it must still change exactly as the preview showed, checked again after the import has written. Otherwise it is refused and nothing changes.</summary>
public sealed class SettingsTransferServiceGuardTests
{
	private const string StaleMessage = "The project's settings changed after the file was read, so nothing was imported. Check what the import will do, then import again.";
	private const string AnotherProjectMessage = "Another project is open, so nothing was imported.";
	private const string NoProjectMessage = "No project is open, so nothing was imported.";
	private const string NotOpenMessage = "The project is not open.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ImportAsync_WithNoSectionPicked_AsksWhatToImportAndChangesNothing()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(), Token));

		Assert.Equal("Choose what to import.", error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Theory]
	[InlineData(TransferSection.Agents, "The file has no agents.")]
	[InlineData(TransferSection.Gates, "The file has no gates.")]
	[InlineData(TransferSection.Categories, "The file has no categories.")]
	[InlineData(TransferSection.Sandbox, "The file has no sandbox settings.")]
	public async Task ImportAsync_OfAPickedSectionTheFileDoesNotHold_IsRefusedAndChangesNothing(TransferSection missing, string expected)
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings file = await FileAsync(service);
		ProjectSettings settings = new(missing == TransferSection.Agents ? null : file.Agents, missing == TransferSection.Gates ? null : file.Gates, missing == TransferSection.Categories ? null : file.Categories, missing == TransferSection.Sandbox ? null : file.Sandbox);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		Assert.Equal(expected, error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Fact]
	public async Task ImportAsync_OfAPickedSectionThePreviewDoesNotList_IsRefusedAndChangesNothing()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings with { Gates = null });
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Agents, TransferSection.Gates), Token));

		Assert.Equal("What the import does to the gates was not reviewed, so nothing was imported. Check what the import will do, then import again.", error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	// Each change made through an editor after the preview changes what importing that section would do, so the preview no longer describes the import.
	[Theory]
	[InlineData("agent created")]
	[InlineData("agent edited")]
	[InlineData("agent deleted")]
	[InlineData("gate created")]
	[InlineData("gates reordered")]
	[InlineData("category created")]
	[InlineData("category recolored")]
	[InlineData("sandbox saved as the file's")]
	[InlineData("sandbox saved with a third Dockerfile")]
	public async Task ImportAsync_AfterTheProjectChangedSinceThePreview_IsRefusedAsStaleAndChangesNothing(string change)
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		await ChangeThroughAnEditorAsync(database, change, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		Assert.Equal(StaleMessage, error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Fact]
	public async Task ImportAsync_AfterAChangeToASectionNotPicked_StillImportsThePickedSections()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await new AgentService(database.ContextFactory, database.Clock).CreateAsync(SettingsTransferServiceTestData.Agent("Late"), Token));

		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Gates, TransferSection.Categories), Token));

		Assert.NotNull(await SettingsTransferServiceTestData.FindAgentAsync(database, "Late"));
		Assert.Equal("Build,Test,Lint", await GateTestData.ReadNamesAsync(database));
	}

	// The editors' rules are checked section by section, so a rule broken only in a section that is not picked stops nothing.
	[Fact]
	public async Task ImportAsync_OfAFileThatBreaksARuleOnlyInASectionNotPicked_ImportsThePickedSections()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings file = await FileAsync(service);
		ProjectSettings settings = file with { Gates = [.. (file.Gates ?? []).Select(gate => gate with { TimeoutMinutes = 0 })] };
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, settings, SettingsTransferServiceTestData.Only(TransferSection.Agents, TransferSection.Categories, TransferSection.Sandbox)));

		before.AssertUnchanged(TransferSection.Gates, await SettingsTransferServiceState.ReadAsync(database));
		Assert.NotNull(await SettingsTransferServiceTestData.FindAgentAsync(database, "Newcomer"));
		Assert.Contains("Research", (await SettingsTransferServiceTestData.ReadCategoriesAsync(database)).Select(category => category.Name));
		Assert.Equal(SettingsTransferServiceTestData.Dockerfile("RUN echo imported"), Assert.Single(await SandboxSettingsRows.ReadAsync(database)).Dockerfile);
	}

	// The write lands as the import begins its transaction, after it has made its context: the plan the import makes inside the transaction sees it.
	[Fact]
	public async Task ImportAsync_WhenAnAgentIsCreatedAsItBeginsItsTransaction_IsRefusedAsStale()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		interceptor.Arm("INSERT INTO \"Agents\" (\"Name\", \"Harness\", \"Authentication\", \"Command\", \"Arguments\", \"CanWork\", \"CanReview\", \"CanMerge\") VALUES ('Late', 0, 1, 'claude', '', 1, 1, 0)");

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		interceptor.AssertFired();
		Assert.Equal(StaleMessage, error);
		Assert.NotNull(await SettingsTransferServiceTestData.FindAgentAsync(database, "Late"));
		Assert.Null(await SettingsTransferServiceTestData.FindAgentAsync(database, "Newcomer"));
	}

	// The check after the write holds the import to the file: a change that lands inside the import's transaction, after its plan and before its own writes, makes it roll everything back.
	[Fact]
	public async Task ImportAsync_WhenTheProjectChangesWhileItWrites_IsRefusedAndChangesNothing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();
		interceptor.Arm("INSERT INTO \"Categories\" (\"Name\", \"Color\") VALUES ('Sneaked', '#000000')");

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		interceptor.AssertFired();
		Assert.Equal("The project's settings changed while they were imported, so nothing was imported. Import again.", error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ImportAsync_WhenAnotherProjectWasOpenedOrTheProjectClosedSinceThePreview_IsRefusedAndChangesNothing(bool anotherOpened)
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		OpenProject openProject = StandInProject.Open();
		SettingsTransferService service = new(database.ContextSource, openProject, database.Clock);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		openProject.Set(anotherOpened ? new Project { Id = SettingsTransferServiceTestData.ProjectId + 1, Name = "other", Folder = Path.Combine(Path.GetTempPath(), "pyxie-other-project"), IsOpen = true } : null);

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		Assert.Equal(anotherOpened ? AnotherProjectMessage : NoProjectMessage, error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Fact]
	public async Task ImportAsync_WhenTheProjectClosesAfterTheServiceReadIt_WorksOnThatProjectInsteadOfFailing()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		OpenProject openProject = StandInProject.Open();
		SettingsTransferService service = new(database.ContextSource, openProject, database.Clock);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		Project read = Assert.IsType<Project>(openProject.Current);
		database.ContextSource.BeforeCreate = _ => openProject.Set(null);

		ServiceResult result = await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token);

		ResultAssert.Succeeded(result);
		Assert.Equal(read.Id, database.ContextSource.Requested[^1].Id);
		Assert.Null(openProject.Current);
	}

	[Fact]
	public async Task ImportAsync_OfAPreviewMadeForAnotherProject_IsRefusedAndChangesNothing()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview with { ProjectId = SettingsTransferServiceTestData.ProjectId + 1 }, SettingsTransferServiceTestData.Every, Token));

		Assert.Equal(AnotherProjectMessage, error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExportAsync_ForAProjectThatIsNotOpen_IsRefused(bool anotherIsOpen)
	{
		await using TestDatabase database = new();
		SettingsTransferService service = new(database.ContextSource, anotherIsOpen ? StandInProject.Open() : new OpenProject(), database.Clock);

		string error = ResultAssert.Failed(await service.ExportAsync(SettingsTransferServiceTestData.ProjectId + 1, Token));

		Assert.Equal(NotOpenMessage, error);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task PreviewAsync_ForAProjectThatIsNotOpen_IsRefused(bool anotherIsOpen)
	{
		await using TestDatabase database = new();
		SettingsTransferService service = new(database.ContextSource, anotherIsOpen ? StandInProject.Open() : new OpenProject(), database.Clock);

		string error = ResultAssert.Failed(await service.PreviewAsync(SettingsTransferServiceTestData.ProjectId + 1, new ProjectSettings(null, [GateTestInput.Create("Build")], null, null), Token));

		Assert.Equal(NotOpenMessage, error);
	}

	// Two agents, two gates, the seeded categories, saved sandbox settings, and a card in the Queue, so every section has something to lose.
	private static async Task ArrangeAsync(TestDatabase database)
	{
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: true, canMerge: true);
		await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true);
		await GateTestData.AddOrderedAsync(database, "Build", "Test");
		ResultAssert.Succeeded(await new SandboxSettingsService(database.ContextFactory, database.Clock).SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo stored")), expectedVersion: 0, Token));
		await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, worker.Id);
	}

	// The project's own export with something added to every section and another Dockerfile, so an import of any section would change the project.
	private static async Task<ProjectSettings> FileAsync(SettingsTransferService service)
	{
		ProjectSettings exported = await SettingsTransferServiceTestData.ExportAsync(service);

		return new ProjectSettings(
			[.. exported.Agents ?? [], SettingsTransferServiceTestData.Agent("Newcomer")],
			[.. exported.Gates ?? [], GateTestInput.Create("Lint", "dotnet", "format")],
			[.. exported.Categories ?? [], SettingsTransferServiceTestData.Category("Research", "#123456")],
			new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));
	}

	private static async Task ChangeThroughAnEditorAsync(TestDatabase database, string change, ProjectSettings settings)
	{
		AgentService agents = new(database.ContextFactory, database.Clock);
		GateService gates = new(database.ContextFactory);
		CategoryService categories = new(database.ContextFactory);
		switch (change)
		{
			case "agent created":
				ResultAssert.Succeeded(await agents.CreateAsync(SettingsTransferServiceTestData.Agent("Late"), Token));
				break;

			case "agent edited":
				Agent reviewer = (await agents.ListAsync(Token)).Single(agent => agent.Name == "Reviewer");
				ResultAssert.Succeeded(await agents.UpdateAsync(reviewer.Id, AgentInput.From(reviewer) with { Command = "reviewer-next" }, Token));
				break;

			case "agent deleted":
				ResultAssert.Succeeded(await agents.DeleteAsync((await agents.ListAsync(Token)).Single(agent => agent.Name == "Reviewer").Id, Token));
				break;

			case "gate created":
				ResultAssert.Succeeded(await gates.CreateAsync(GateTestInput.Create("Late"), Token));
				break;

			case "gates reordered":
				ResultAssert.Succeeded(await gates.MoveAsync((await gates.ListAsync(Token)).Single(gate => gate.Name == "Test").Id, 0, Token));
				break;

			case "category created":
				ResultAssert.Succeeded(await categories.CreateAsync("Late", HexColor.Parse("#654321"), Token));
				break;

			case "category recolored":
				ResultAssert.Succeeded(await categories.SetColorAsync(PyxieSeedData.ChoreCategoryId, CategoryColors.Green, Token));
				break;

			case "sandbox saved with a third Dockerfile":
				SandboxSettingsService otherSandbox = new(database.ContextFactory, database.Clock);
				ResultAssert.Succeeded(await otherSandbox.SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo later")), (await otherSandbox.GetAsync(Token)).Version, Token));
				break;

			case "sandbox saved as the file's":
				SandboxSettingsService sandbox = new(database.ContextFactory, database.Clock);
				ResultAssert.Succeeded(await sandbox.SaveAsync(settings.Sandbox ?? new SandboxSettingsInput(null), (await sandbox.GetAsync(Token)).Version, Token));
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(change), change, "No such change.");
		}
	}
}
