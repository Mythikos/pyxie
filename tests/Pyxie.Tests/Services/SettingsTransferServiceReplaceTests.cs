using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An imported section replaces the project's: the file's items match the project's by name, ignoring case, and are updated in place so cards keep referring to them; the file's other items are added and the project's other items removed; gates end in the file's order; and a section that is not picked is not touched.</summary>
public sealed class SettingsTransferServiceReplaceTests
{
	[Fact]
	public async Task ImportAsync_OfGatesThatChangeNothing_LeavesTheirStoredPositionsAsTheyAre()
	{
		await using TestDatabase database = new();
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		await GateTestData.AddAtAsync(database, "Build", sortOrder: 3);
		await GateTestData.AddAtAsync(database, "Tests", sortOrder: 7);
		ProjectSettings own = await SettingsTransferServiceTestData.ExportAsync(service);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, own, SettingsTransferServiceTestData.Only(TransferSection.Gates)));

		Assert.Equal([("Build", 3), ("Tests", 7)], (await GateTestData.ReadAsync(database)).Select(gate => (gate.Name, gate.SortOrder)));
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ImportAsync_Agents_UpdatesAMatchedAgentInPlaceSoItsCardsKeepIt()
	{
		await using TestDatabase database = new();
		Agent builder = await TestData.AddAgentAsync(database, "Builder", canWork: true, canReview: true);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, builder.Id);
		WorkCard working = await TestData.AddCardAsync(database, "Working", CardLane.InProgress, PyxieSeedData.FeatureCategoryId, builder.Id, builder.Id);
		AgentInput incoming = SettingsTransferServiceTestData.Agent("Builder", HarnessKind.Codex, "codex", "exec --full-auto {CARD_NAME}");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, new ProjectSettings([incoming], null, null, null), SettingsTransferServiceTestData.Only(TransferSection.Agents)));

		Agent? stored = await TestData.FindAgentAsync(database, builder.Id);
		Assert.True(stored is not null, "A matched agent is updated in place, not replaced.");
		Assert.Equal(incoming, AgentInput.From(stored));
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, working, expectedCardCount: 2);
	}

	[Fact]
	public async Task ImportAsync_Agents_AddsTheFilesNewAgentsAndRemovesTheProjectsOthers()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Old", canWork: true, canReview: true);
		Agent kept = await TestData.AddAgentAsync(database, "Kept", canWork: true, canReview: true);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, kept.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new([SettingsTransferServiceTestData.Agent("Kept"), SettingsTransferServiceTestData.Agent("New", HarnessKind.DeepSeek, "dsh", canReview: false)], null, null, null);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Agents), Token));

		List<Agent> agents = await SettingsTransferServiceTestData.ReadAgentsAsync(database);
		Assert.Equal(["Kept", "New"], agents.Select(agent => agent.Name));
		Assert.Equal(kept.Id, agents[0].Id);
		Assert.Equal(SettingsTransferServiceTestData.Agent("New", HarnessKind.DeepSeek, "dsh", canReview: false), AgentInput.From(agents[1]));
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 1);
	}

	[Fact]
	public async Task ImportAsync_Agents_RenamesAnAgentInPlaceWhenOnlyTheCaseOfItsNameDiffers()
	{
		await using TestDatabase database = new();
		Agent builder = await TestData.AddAgentAsync(database, "Builder", canWork: true, canReview: true);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, builder.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new([SettingsTransferServiceTestData.Agent("BUILDER")], null, null, null);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Agents), Token));

		SectionChanges changes = ChangesOf(preview, TransferSection.Agents);
		Assert.Empty(changes.Added);
		Assert.Empty(changes.Removed);
		Assert.Equal(["BUILDER"], changes.Changed);
		Assert.Equal([(builder.Id, "BUILDER")], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => (agent.Id, agent.Name)));
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 1);
	}

	// Names are stored exactly, so a project can hold names that differ only in case; the file's name takes the agent with exactly its name first, and otherwise the first that differs only in case.
	[Theory]
	[InlineData("builder", 1)]
	[InlineData("Builder", 0)]
	[InlineData("BUILDER", 0)]
	public async Task ImportAsync_Agents_MatchesTheExactNameFirstAndOtherwiseTheFirstNameThatDiffersOnlyInCase(string fileName, int keptIndex)
	{
		await using TestDatabase database = new();
		Agent[] stored = [await TestData.AddAgentAsync(database, "Builder", canWork: true, canReview: true), await TestData.AddAgentAsync(database, "builder", canWork: true, canReview: true)];
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, new ProjectSettings([SettingsTransferServiceTestData.Agent(fileName)], null, null, null), SettingsTransferServiceTestData.Only(TransferSection.Agents)));

		Assert.Equal([(stored[keptIndex].Id, fileName)], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => (agent.Id, agent.Name)));
	}

	// The unique indexes on the name and on the merge role check each row as it is written, in whatever order the writes are sent, so both orders of the two rows are tried.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ImportAsync_Agents_TwoAgentsExchangeEveryFieldButTheirNamesIncludingTheMergeRole(bool firstIsTheMergeAgent)
	{
		await using TestDatabase database = new();
		Agent first = await TestData.AddAgentAsync(database, "First", canWork: true, canReview: true, HarnessKind.ClaudeCode, "claude", "-p {CARD_NAME}", canMerge: firstIsTheMergeAgent);
		Agent second = await TestData.AddAgentAsync(database, "Second", canWork: false, canReview: false, HarnessKind.Codex, "codex", "exec {BRANCH}", canMerge: !firstIsTheMergeAgent);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, first.Id, first.Id);
		AgentInput firstAsSecond = AgentInput.From(second) with { Name = "First" };
		AgentInput secondAsFirst = AgentInput.From(first) with { Name = "Second" };
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		DateTime importedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, new ProjectSettings([firstAsSecond, secondAsFirst], null, null, null), SettingsTransferServiceTestData.Only(TransferSection.Agents)));

		Assert.Equal([firstAsSecond, secondAsFirst], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(AgentInput.From));
		Assert.Equal([first.Id, second.Id], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => agent.Id));
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));

		// First lost both roles, so the finished card that had it in both lets go of it, once.
		WorkCard? released = await TestData.FindCardAsync(database, complete.Id);
		Assert.Equal((null, null, complete.Version + 1, importedAt), (released?.WorkerAgentId, released?.ReviewerAgentId, released?.Version, released?.UpdatedUtc));
	}

	[Fact]
	public async Task ImportAsync_Agents_AnAgentRenamedInTheFileIsRemovedAndANewOneAddedAndItsFinishedCardsLetGoOfIt()
	{
		await using TestDatabase database = new();
		Agent alpha = await TestData.AddAgentAsync(database, "Alpha", canWork: true, canReview: true);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, alpha.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.FeatureCategoryId, workerAgentId: null, reviewerAgentId: alpha.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new([AgentInput.From(alpha) with { Name = "Beta" }], null, null, null);
		DateTime importedAt = database.Clock.Tick();

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Agents), Token));

		SectionChanges changes = ChangesOf(preview, TransferSection.Agents);
		Assert.Equal(["Beta"], changes.Added);
		Assert.Empty(changes.Changed);
		Assert.Equal(["Alpha"], changes.Removed);
		List<Agent> agents = await SettingsTransferServiceTestData.ReadAgentsAsync(database);
		Assert.Equal(["Beta"], agents.Select(agent => agent.Name));
		Assert.NotEqual(alpha.Id, agents[0].Id);
		WorkCard? completeAfter = await TestData.FindCardAsync(database, complete.Id);
		WorkCard? closedAfter = await TestData.FindCardAsync(database, closed.Id);
		Assert.Equal((null, complete.Version + 1, importedAt), (completeAfter?.WorkerAgentId, completeAfter?.Version, completeAfter?.UpdatedUtc));
		Assert.Equal((null, closed.Version + 1, importedAt), (closedAfter?.ReviewerAgentId, closedAfter?.Version, closedAfter?.UpdatedUtc));
	}

	[Fact]
	public async Task ImportAsync_Agents_TakingARoleFromAnAgentReleasesOnlyThatRoleAndOnlyOnFinishedCards()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, agent.Id, agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.FeatureCategoryId, agent.Id, agent.Id);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, agent.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		DateTime importedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, new ProjectSettings([AgentInput.From(agent) with { CanReview = false }], null, null, null), SettingsTransferServiceTestData.Only(TransferSection.Agents)));

		foreach (WorkCard finished in new[] { complete, closed })
		{
			WorkCard? stored = await TestData.FindCardAsync(database, finished.Id);
			Assert.Equal((agent.Id, null, finished.Version + 1, importedAt), (stored?.WorkerAgentId, stored?.ReviewerAgentId, stored?.Version, stored?.UpdatedUtc));
		}

		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 3);
	}

	[Fact]
	public async Task ImportAsync_Agents_AnAgentThatKeepsItsRolesLeavesItsFinishedCardsAlone()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, agent.Id, agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.FeatureCategoryId, agent.Id, agent.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, new ProjectSettings([AgentInput.From(agent) with { Command = "claude-next", Harness = HarnessKind.Other, Authentication = AgentAuthentication.None }], null, null, null), SettingsTransferServiceTestData.Only(TransferSection.Agents)));

		await CardAssert.UnchangedAsync(database, complete, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, closed, expectedCardCount: 2);
	}

	[Fact]
	public async Task ImportAsync_Gates_EndInTheFilesOrderNumberedFromZeroAndKeepTheMatchedGates()
	{
		await using TestDatabase database = new();
		Gate build = await GateTestData.AddAtAsync(database, "Build", sortOrder: 3);
		Gate lint = await GateTestData.AddAtAsync(database, "Lint", sortOrder: 9);
		Gate test = await GateTestData.AddAtAsync(database, "Test", sortOrder: 9);
		await GateTestData.AddAtAsync(database, "Old", sortOrder: 20);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(null, [GateTestInput.Create("Test"), GateTestInput.Create("New", "npm", "test", 5), GateTestInput.Create("Build"), GateTestInput.Create("Lint")], null, null);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, settings, SettingsTransferServiceTestData.Only(TransferSection.Gates)));

		List<Gate> gates = await GateTestData.ReadAsync(database);
		Assert.Equal(["Test", "New", "Build", "Lint"], gates.Select(gate => gate.Name));
		Assert.Equal([0, 1, 2, 3], gates.Select(gate => gate.SortOrder));
		Assert.Equal([test.Id, build.Id, lint.Id], gates.Where(gate => gate.Name != "New").Select(gate => gate.Id));
		Assert.Equal(settings.Gates, gates.Select(GateInput.From));
	}

	[Fact]
	public async Task ImportAsync_Gates_APureReorderIsPreviewedAsReorderedOnlyAndKeepsEveryGate()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> stored = await GateTestData.AddOrderedAsync(database, "Build", "Lint", "Test");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(null, [.. new[] { "Test", "Build", "Lint" }.Select(name => GateInput.From(stored[name]))], null, null);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Gates), Token));

		SectionChanges changes = ChangesOf(preview, TransferSection.Gates);
		Assert.True(changes is { Added.Count: 0, Changed.Count: 0, Removed.Count: 0, Reordered: true }, "A file that only lists the same gates in another order is a reorder and nothing else.");
		List<Gate> gates = await GateTestData.ReadAsync(database);
		Assert.Equal([stored["Test"].Id, stored["Build"].Id, stored["Lint"].Id], gates.Select(gate => gate.Id));
		Assert.Equal([0, 1, 2], gates.Select(gate => gate.SortOrder));
	}

	[Fact]
	public async Task ImportAsync_Gates_UpdatesMatchedGatesInPlaceIncludingACaseOnlyRename()
	{
		await using TestDatabase database = new();
		Gate build = await GateTestData.AddAtAsync(database, "Build", sortOrder: 0);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		GateInput incoming = GateTestInput.Create("build", "make", "all", 90, isEnabled: false);
		ProjectSettings settings = new(null, [incoming], null, null);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Gates), Token));

		Assert.Equal(["build"], ChangesOf(preview, TransferSection.Gates).Changed);
		Gate stored = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.Equal((build.Id, incoming), (stored.Id, GateInput.From(stored)));
	}

	[Fact]
	public async Task ImportAsync_Categories_UpdatesMatchedCategoriesInPlaceSoTheirCardsKeepThem()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.BugCategoryId, agent.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(null, null, [SettingsTransferServiceTestData.Category("feature", "#1F3A93"), new CategoryInput("Bug", CategoryColors.Pink), SettingsTransferServiceTestData.Category("Research", "#FFAA00")], null);
		database.Clock.Tick();

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		ResultAssert.Succeeded(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Only(TransferSection.Categories), Token));

		SectionChanges changes = ChangesOf(preview, TransferSection.Categories);
		Assert.Equal(["Research"], changes.Added);
		Assert.Equal(["feature"], changes.Changed);
		Assert.Equal(["Chore"], changes.Removed);
		List<Category> categories = await SettingsTransferServiceTestData.ReadCategoriesAsync(database);
		Assert.Equal([(PyxieSeedData.FeatureCategoryId, "feature", "#1F3A93"), (PyxieSeedData.BugCategoryId, "Bug", CategoryColors.Pink.ToString())], categories.Take(2).Select(category => (category.Id, category.Name, category.Color.ToString())));
		Assert.Equal(["feature", "Bug", "Research"], categories.Select(category => category.Name));
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, closed, expectedCardCount: 2);
	}

	[Theory]
	[InlineData(TransferSection.Agents)]
	[InlineData(TransferSection.Gates)]
	[InlineData(TransferSection.Categories)]
	public async Task ImportAsync_AnEmptySectionRemovesEveryItemOfIt(TransferSection section)
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		await GateTestData.AddOrderedAsync(database, "Build", "Test");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(section == TransferSection.Agents ? [] : null, section == TransferSection.Gates ? [] : null, section == TransferSection.Categories ? [] : null, null);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, settings, SettingsTransferServiceTestData.Only(section)));

		SettingsTransferServiceState after = await SettingsTransferServiceState.ReadAsync(database);
		IReadOnlyList<string> emptied = section switch
		{
			TransferSection.Agents => after.Agents,
			TransferSection.Gates => after.Gates,
			_ => after.Categories,
		};
		Assert.True(emptied.Count == 0, $"An empty {section} section removes every item of it, but these remain: {string.Join("; ", emptied)}");
		foreach (TransferSection other in Enum.GetValues<TransferSection>().Where(other => other != section))
		{
			before.AssertUnchanged(other, after);
		}
	}

	[Theory]
	[InlineData(TransferSection.Agents)]
	[InlineData(TransferSection.Gates)]
	[InlineData(TransferSection.Categories)]
	[InlineData(TransferSection.Sandbox)]
	public async Task ImportAsync_OfOneSection_LeavesEveryOtherSectionAndTheCardsUntouched(TransferSection section)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true, canMerge: true);
		await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, agent.Id, agent.Id);
		await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.BugCategoryId, agent.Id, agent.Id);
		await GateTestData.AddOrderedAsync(database, "Build", "Test");
		ResultAssert.Succeeded(await new SandboxSettingsService(database.ContextFactory, database.Clock).SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo stored")), expectedVersion: 0, Token));
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		// Every section of the file differs from the project's, so importing any of them would show.
		ProjectSettings settings = new(
			[AgentInput.From(agent) with { Command = "claude-next" }, SettingsTransferServiceTestData.Agent("Extra")],
			[GateTestInput.Create("Test"), GateTestInput.Create("Build", "make"), GateTestInput.Create("Lint")],
			[.. SettingsTransferServiceTestData.SeededCategories(), SettingsTransferServiceTestData.Category("Research", "#123456")],
			new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, settings, SettingsTransferServiceTestData.Only(section)));

		SettingsTransferServiceState after = await SettingsTransferServiceState.ReadAsync(database);
		ProjectSettings exported = await SettingsTransferServiceTestData.ExportAsync(service);
		// An export lists agents and categories by name and gates in the order they run, so the picked section exports as the file holds it in that order.
		bool imported = section switch
		{
			TransferSection.Agents => (exported.Agents ?? []).SequenceEqual((settings.Agents ?? []).OrderBy(agent => agent.Name, StringComparer.Ordinal)),
			TransferSection.Gates => (exported.Gates ?? []).SequenceEqual(settings.Gates ?? []),
			TransferSection.Categories => (exported.Categories ?? []).SequenceEqual((settings.Categories ?? []).OrderBy(category => category.Name, StringComparer.Ordinal)),
			_ => exported.Sandbox == settings.Sandbox,
		};
		Assert.True(imported, $"The picked {section} section must be imported.");
		foreach (TransferSection other in Enum.GetValues<TransferSection>().Where(other => other != section))
		{
			before.AssertUnchanged(other, after);
		}

		Assert.True(before.Cards.SequenceEqual(after.Cards, StringComparer.Ordinal), $"No card changes when no agent loses a role or goes.\nBefore:\n  {string.Join("\n  ", before.Cards)}\nAfter:\n  {string.Join("\n  ", after.Cards)}");
	}

	private static SectionChanges ChangesOf(SettingsImportPreview preview, TransferSection section)
	{
		SectionPreview? found = preview.Find(section);
		Assert.True(found is not null, $"The preview lists every section the file holds, {section} among them.");

		return found.Changes;
	}
}
