using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An export holds every section of the open project as a settings file holds it: agents and categories by name as the editors list them, gates in the order they run, and the Dockerfile in effect.</summary>
public sealed class SettingsTransferServiceExportTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ExportAsync_ListsTheAgentsByNameAsTheAgentEditorDoes()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Zed", canWork: true, canReview: false);
		await TestData.AddAgentAsync(database, "alpha", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Beta", canWork: false, canReview: true);
		await TestData.AddAgentAsync(database, "Mid", canWork: true, canReview: true);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		Assert.NotNull(settings.Agents);
		Assert.Equal(["Beta", "Mid", "Zed", "alpha"], settings.Agents.Select(agent => agent.Name));
		Assert.Equal((await new AgentService(database.ContextFactory, database.Clock).ListAsync(Token)).Select(agent => agent.Name), settings.Agents.Select(agent => agent.Name));
	}

	[Fact]
	public async Task ExportAsync_WritesEveryFieldOfEachAgentAsStored()
	{
		await using TestDatabase database = new();
		Agent codex = await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: false, HarnessKind.Codex, "codex", "exec --full-auto {CARD_NAME} {BRANCH}");
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, HarnessKind.Other, "merge-bot", "--attempt {ATTEMPT}", canMerge: true);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true, HarnessKind.DeepSeek, "dsh", "run {STAGE} {CARD_ID}");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		Assert.Equal([AgentInput.From(codex), AgentInput.From(merger), AgentInput.From(reviewer)], settings.Agents);
	}

	[Fact]
	public async Task ExportAsync_ListsTheCategoriesByNameInTheirColorsAsTheCategoryEditorDoes()
	{
		await using TestDatabase database = new();
		CategoryService categories = new(database.ContextFactory);
		ResultAssert.Succeeded(await categories.CreateAsync("Research", HexColor.Parse("#1F3A93"), Token));
		ResultAssert.Succeeded(await categories.CreateAsync("chores", HexColor.Parse("#000000"), Token));
		ResultAssert.Succeeded(await categories.CreateAsync("Alpha", HexColor.Parse("#ABCDEF"), Token));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		Assert.NotNull(settings.Categories);
		Assert.Equal([("Alpha", "#ABCDEF"), ("Bug", CategoryColors.Pink.ToString()), ("Chore", CategoryColors.Neutral.ToString()), ("Feature", CategoryColors.Green.ToString()), ("Research", "#1F3A93"), ("chores", "#000000")], settings.Categories.Select(category => (category.Name, category.Color.ToString())));
		Assert.Equal((await categories.ListAsync(Token)).Select(category => category.Name), settings.Categories.Select(category => category.Name));
	}

	[Fact]
	public async Task ExportAsync_ListsTheGatesInTheOrderTheyRunWithStoredGapsAndTiesCollapsed()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Lint", sortOrder: 7, command: "npm", arguments: "run lint");
		await GateTestData.AddAtAsync(database, "Build", sortOrder: 2, command: "dotnet", arguments: "build", timeoutMinutes: 12);
		await GateTestData.AddAtAsync(database, "Test", sortOrder: 7, isEnabled: false);
		await GateTestData.AddAtAsync(database, "Pack", sortOrder: 40, command: "dotnet", arguments: "pack", timeoutMinutes: 240);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		// Positions are not part of a file: the file's order is the run order, ties settled by which gate was added first, as the gate list shows them.
		Assert.Equal(
			[
				GateTestInput.Create("Build", "dotnet", "build", 12),
				GateTestInput.Create("Lint", "npm", "run lint"),
				GateTestInput.Create("Test", isEnabled: false),
				GateTestInput.Create("Pack", "dotnet", "pack", 240),
			],
			settings.Gates);
		Assert.Equal((await new GateService(database.ContextFactory).ListAsync(Token)).Select(gate => gate.Name), settings.Gates?.Select(gate => gate.Name) ?? []);
	}

	[Fact]
	public async Task ExportAsync_WhenNoSandboxSettingsWereSaved_WritesTheDefaultDockerfileNormalizedAndSavesNothing()
	{
		await using TestDatabase database = new();
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		Assert.Equal(SettingsTransferServiceTestData.DefaultDockerfile, settings.Sandbox?.Dockerfile);
		Assert.True(settings.Sandbox?.Dockerfile?.EndsWith("\n", StringComparison.Ordinal) == true && !settings.Sandbox.Dockerfile.Contains('\r', StringComparison.Ordinal), "The exported Dockerfile is normalized: LF line endings and one final line break.");
		Assert.Empty(await SandboxSettingsRows.ReadAsync(database));
	}

	[Fact]
	public async Task ExportAsync_WhenSandboxSettingsWereSaved_WritesTheSavedDockerfile()
	{
		await using TestDatabase database = new();
		SandboxSettingsService sandbox = new(database.ContextFactory, database.Clock);
		ResultAssert.Succeeded(await sandbox.SaveAsync(new SandboxSettingsInput("FROM ${PYXIE_BASE}\r\nRUN make\r\n\r\n"), expectedVersion: 0, Token));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		Assert.Equal("FROM ${PYXIE_BASE}\nRUN make\n", settings.Sandbox?.Dockerfile);
	}

	[Fact]
	public async Task ExportAsync_OfAProjectWithNoAgentsOrGates_HoldsEverySectionWithTheEmptyOnesEmpty()
	{
		await using TestDatabase database = new();
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ProjectSettings settings = await SettingsTransferServiceTestData.ExportAsync(service);

		Assert.Equal(Enum.GetValues<TransferSection>(), settings.Sections);
		Assert.True(settings.Agents is { Count: 0 }, "A project without agents exports an empty agents section, which an import takes to mean remove them all.");
		Assert.True(settings.Gates is { Count: 0 }, "A project without gates exports an empty gates section, which an import takes to mean remove them all.");
	}

	[Fact]
	public async Task ExportAsync_ForAProjectThatIsNotTheOpenOne_IsRefused()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		string error = ResultAssert.Failed(await service.ExportAsync(SettingsTransferServiceTestData.ProjectId + 1, Token));

		Assert.Equal("The project is not open.", error);
	}

	[Fact]
	public async Task ExportAsync_WhenNoProjectIsOpen_IsRefused()
	{
		await using TestDatabase database = new();
		SettingsTransferService service = new(database.ContextSource, new OpenProject(), database.Clock);

		string error = ResultAssert.Failed(await service.ExportAsync(SettingsTransferServiceTestData.ProjectId, Token));

		Assert.Equal("The project is not open.", error);
	}
}
