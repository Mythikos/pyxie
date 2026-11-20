using System.Text;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A project's settings survive the trip through a settings file: exported, written, read back, and imported into another project, they export again to the same bytes; and importing a project's own export changes nothing at all.</summary>
public sealed class SettingsTransferServiceRoundTripTests
{
	private const string SavedDockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get update && apt-get install -y make && echo 'ready' > /tmp/ready\n";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ImportingAnExportIntoAFreshProject_ExportsTheSameBytes()
	{
		await using TestDatabase source = new();
		await ArrangeEveryKindOfSettingAsync(source);
		await using TestDatabase target = new();
		byte[] written = SettingsFileWriter.Write(await SettingsTransferServiceTestData.ExportAsync(SettingsTransferServiceTestData.Create(source)));
		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(written));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(target);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, read);
		ResultAssert.Succeeded(await service.ImportAsync(read, preview, SettingsTransferServiceTestData.Every, Token));

		Assert.True(preview.Sections.All(section => section.CanImport), $"Every section of an exported file can be imported into a fresh project, but: {string.Join(" ", preview.Sections.SelectMany(section => section.Problems))}");
		byte[] again = SettingsFileWriter.Write(await SettingsTransferServiceTestData.ExportAsync(service));
		Assert.Equal(Encoding.UTF8.GetString(written), Encoding.UTF8.GetString(again));
		Assert.True(written.AsSpan().SequenceEqual(again), "The second export must be byte for byte the first.");
	}

	[Fact]
	public async Task ImportingAnExportIntoAProjectWithOtherSettings_ReplacesThemAndExportsTheSameBytes()
	{
		await using TestDatabase source = new();
		await ArrangeEveryKindOfSettingAsync(source);
		await using TestDatabase target = new();
		await TestData.AddAgentAsync(target, "Stranger", canWork: true, canReview: true, canMerge: true);
		await TestData.AddAgentAsync(target, "claude", canWork: false, canReview: true, HarnessKind.Other, "old");
		await GateTestData.AddAtAsync(target, "Smoke", sortOrder: 0);
		await GateTestData.AddAtAsync(target, "test", sortOrder: 3, command: "make", arguments: "check");
		ResultAssert.Succeeded(await new CategoryService(target.ContextFactory).CreateAsync("Spike", HexColor.Parse("#123456"), Token));
		ResultAssert.Succeeded(await new SandboxSettingsService(target.ContextFactory, target.Clock).SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo other")), expectedVersion: 0, Token));
		byte[] written = SettingsFileWriter.Write(await SettingsTransferServiceTestData.ExportAsync(SettingsTransferServiceTestData.Create(source)));
		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(written));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(target);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, read, SettingsTransferServiceTestData.Every));

		byte[] again = SettingsFileWriter.Write(await SettingsTransferServiceTestData.ExportAsync(service));
		Assert.Equal(Encoding.UTF8.GetString(written), Encoding.UTF8.GetString(again));
	}

	[Fact]
	public async Task ImportingTheProjectsOwnExport_PreviewsNoChangeAndChangesNothing()
	{
		await using TestDatabase database = new();
		(Agent claude, Agent reviewer) = await ArrangeEveryKindOfSettingAsync(database);
		await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, claude.Id, reviewer.Id);
		await TestData.AddCardAsync(database, "Reviewing", CardLane.Review, PyxieSeedData.FeatureCategoryId, claude.Id, reviewer.Id);
		await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.BugCategoryId, claude.Id, reviewer.Id);
		await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.BugCategoryId, claude.Id, reviewer.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(SettingsFileWriter.Write(await SettingsTransferServiceTestData.ExportAsync(service))));
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, read);
		ResultAssert.Succeeded(await service.ImportAsync(read, preview, SettingsTransferServiceTestData.Every, Token));

		Assert.Equal(Enum.GetValues<TransferSection>(), preview.Sections.Select(section => section.Section));
		Assert.True(preview.Sections.All(section => !section.Changes.HasChanges && section.CanImport), $"A project's own export changes nothing and nothing stops it, but the preview lists: {string.Join("; ", preview.Sections.Select(Describe))}");
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Fact]
	public async Task ImportingTheProjectsOwnExport_WhenNoSandboxSettingsWereSaved_SavesNone()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, agent.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings exported = await SettingsTransferServiceTestData.ExportAsync(service);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, exported, SettingsTransferServiceTestData.Every));

		Assert.Empty(await SandboxSettingsRows.ReadAsync(database));
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Fact]
	public async Task ImportingTheProjectsOwnExport_WithGapsAndTiesInTheStoredGateOrder_PreviewsNoChangeAndKeepsTheRunOrder()
	{
		await using TestDatabase database = new();
		Gate lint = await GateTestData.AddAtAsync(database, "Lint", sortOrder: 7);
		Gate build = await GateTestData.AddAtAsync(database, "Build", sortOrder: 2);
		Gate test = await GateTestData.AddAtAsync(database, "Test", sortOrder: 7);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings exported = await SettingsTransferServiceTestData.ExportAsync(service);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, exported);
		ResultAssert.Succeeded(await service.ImportAsync(exported, preview, SettingsTransferServiceTestData.Only(TransferSection.Gates), Token));

		Assert.False(preview.Find(TransferSection.Gates)?.Changes.HasChanges ?? true, "Gaps and ties in the stored positions are not a change of order.");
		Assert.Equal([build.Id, lint.Id, test.Id], (await GateTestData.ReadAsync(database)).Select(gate => gate.Id));
	}

	// Agents of every harness, with tokens in their arguments, a merge agent and a reviewer-only agent; gates with one disabled, a custom timeout, and an order other than the one they were added in; categories renamed, recolored, added, and removed; and a saved Dockerfile. Everything goes through the editors' services, so it is stored as the editors store it.
	private static async Task<(Agent Claude, Agent Reviewer)> ArrangeEveryKindOfSettingAsync(TestDatabase database)
	{
		AgentService agents = new(database.ContextFactory, database.Clock);
		Agent claude = ResultAssert.Succeeded(await agents.CreateAsync(new AgentInput("Claude", HarnessKind.ClaudeCode, "claude", "-p --dangerously-skip-permissions --append-system-prompt \"Card {CARD_NAME} on {BRANCH}\"", CanWork: true, CanReview: true), Token));
		ResultAssert.Succeeded(await agents.CreateAsync(new AgentInput("Codex", HarnessKind.Codex, "codex", "exec --full-auto --stage {STAGE} --attempt {ATTEMPT}", CanWork: true, CanReview: false), Token));
		ResultAssert.Succeeded(await agents.CreateAsync(new AgentInput("DeepSeek", HarnessKind.DeepSeek, "dsh", "run --card {CARD_ID} ${HOME}", CanWork: false, CanReview: false, CanMerge: true), Token));
		Agent reviewer = ResultAssert.Succeeded(await agents.CreateAsync(new AgentInput("Reviewer", HarnessKind.Other, "review-bot", "--strict & 'quoted' <html>", CanWork: false, CanReview: true), Token));

		GateService gates = new(database.ContextFactory);
		ResultAssert.Succeeded(await gates.CreateAsync(GateTestInput.Create("Build", "dotnet", "build -warnaserror"), Token));
		ResultAssert.Succeeded(await gates.CreateAsync(GateTestInput.Create("Lint", "dotnet", "format --verify-no-changes", isEnabled: false), Token));
		Gate test = ResultAssert.Succeeded(await gates.CreateAsync(GateTestInput.Create("Test", "dotnet", "test", timeoutMinutes: 45), Token));
		ResultAssert.Succeeded(await gates.MoveAsync(test.Id, 0, Token));

		CategoryService categories = new(database.ContextFactory);
		ResultAssert.Succeeded(await categories.SetColorAsync(PyxieSeedData.FeatureCategoryId, HexColor.Parse("#1F3A93"), Token));
		ResultAssert.Succeeded(await categories.RenameAsync(PyxieSeedData.BugCategoryId, "Defect", Token));
		ResultAssert.Succeeded(await categories.DeleteAsync(PyxieSeedData.ChoreCategoryId, Token));
		ResultAssert.Succeeded(await categories.CreateAsync("Research", HexColor.Parse("#FFAA00"), Token));

		ResultAssert.Succeeded(await new SandboxSettingsService(database.ContextFactory, database.Clock).SaveAsync(new SandboxSettingsInput(SavedDockerfile), expectedVersion: 0, Token));

		return (claude, reviewer);
	}

	private static string Describe(SectionPreview section)
	{
		SectionChanges changes = section.Changes;

		return $"{section.Section}: added [{string.Join(", ", changes.Added)}] changed [{string.Join(", ", changes.Changed)}] removed [{string.Join(", ", changes.Removed)}] reordered {changes.Reordered} problems [{string.Join(" ", section.Problems)}]";
	}
}
