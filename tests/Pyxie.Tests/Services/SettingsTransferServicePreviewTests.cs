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

/// <summary>A preview says, for each section the file holds and without writing anything, what importing it would add, change, remove, and reorder, and what would stop it, in the words the import would refuse it with.</summary>
public sealed class SettingsTransferServicePreviewTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task PreviewAsync_ListsWhatImportingEachSectionWouldChange()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Charlie", canWork: true, canReview: true);
		Agent alpha = await TestData.AddAgentAsync(database, "Alpha", canWork: true, canReview: true);
		Agent bravo = await TestData.AddAgentAsync(database, "Bravo", canWork: true, canReview: false);
		await TestData.AddAgentAsync(database, "Delta", canWork: false, canReview: true);
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Lint", "Test", "Old");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(
			[SettingsTransferServiceTestData.Agent("Echo"), AgentInput.From(bravo) with { Name = "bravo" }, AgentInput.From(alpha), SettingsTransferServiceTestData.Agent("Foxtrot", canMerge: true)],
			[GateInput.From(gates["Test"]), GateTestInput.Create("New"), GateInput.From(gates["Build"]) with { Arguments = "build" }],
			[SettingsTransferServiceTestData.Category("Research", "#123456"), new CategoryInput("bug", CategoryColors.Pink), new CategoryInput("Feature", HexColor.Parse("#1F3A93"))],
			new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);

		Assert.Equal(SettingsTransferServiceTestData.ProjectId, preview.ProjectId);
		Assert.Equal(Enum.GetValues<TransferSection>(), preview.Sections.Select(section => section.Section));
		Assert.True(preview.Sections.All(section => section.CanImport), $"Nothing stops this import, but: {string.Join(" ", preview.Sections.SelectMany(section => section.Problems))}");

		// Added and changed items are named as the file writes them, in the file's order; removed items as stored, agents and categories in the order they were made and gates in the order they run.
		AssertChanges(preview, TransferSection.Agents, added: ["Echo", "Foxtrot"], changed: ["bravo"], removed: ["Charlie", "Delta"], reordered: false);
		AssertChanges(preview, TransferSection.Gates, added: ["New"], changed: ["Build"], removed: ["Lint", "Old"], reordered: true);
		AssertChanges(preview, TransferSection.Categories, added: ["Research"], changed: ["bug", "Feature"], removed: ["Chore"], reordered: false);
		AssertChanges(preview, TransferSection.Sandbox, added: [], changed: ["Dockerfile"], removed: [], reordered: false);
	}

	[Fact]
	public async Task PreviewAsync_DoesNotReportAReorderWhenTheKeptGatesStayInOrderAroundAddsAndRemoves()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Lint", "Test");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(null, [GateTestInput.Create("First"), GateInput.From(gates["Build"]), GateTestInput.Create("Middle"), GateInput.From(gates["Test"])], null, null);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);

		AssertChanges(preview, TransferSection.Gates, added: ["First", "Middle"], changed: [], removed: ["Lint"], reordered: false);
	}

	[Fact]
	public async Task PreviewAsync_ListsWhatStopsEachSectionInTheWordsTheImportRefusesItWith()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		Agent spare = await TestData.AddAgentAsync(database, "Spare", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, worker.Id, spare.Id);
		await TestData.AddCardAsync(database, "Working", CardLane.InProgress, PyxieSeedData.FeatureCategoryId, spare.Id);
		await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.ChoreCategoryId, spare.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(
			[AgentInput.From(spare) with { CanWork = false, CanReview = false }],
			[GateTestInput.Create("Build")],
			[new CategoryInput("Feature", CategoryColors.Green), new CategoryInput("Bug", CategoryColors.Pink)],
			new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		Assert.Equal(
			[
				"Agent 'Worker' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.",
				"Agent 'Spare' is the worker of 1 card that is not complete and must keep the worker role.",
				"Agent 'Spare' is the reviewer of 1 card that is not complete and must keep the reviewer role.",
			],
			Find(preview, TransferSection.Agents).Problems);
		Assert.Equal(["Category 'Chore' is used by 1 card and cannot be deleted."], Find(preview, TransferSection.Categories).Problems);
		Assert.True(Find(preview, TransferSection.Gates).CanImport && Find(preview, TransferSection.Sandbox).CanImport, "Nothing stops the gates or the sandbox settings.");
		Assert.False(Find(preview, TransferSection.Agents).CanImport || Find(preview, TransferSection.Categories).CanImport, "A section that cards still need cannot be imported.");

		// What stops a section does not hide what it would change.
		AssertChanges(preview, TransferSection.Agents, added: [], changed: ["Spare"], removed: ["Worker"], reordered: false);
		Assert.Equal(string.Join("\n", preview.Sections.SelectMany(section => section.Problems).Prepend("Nothing was imported.")), error);
	}

	[Fact]
	public async Task PreviewAsync_OfASectionThatBreaksARule_ListsItsProblemsAndNoChangesWhileTheOtherSectionsArePreviewed()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: true);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(
			[SettingsTransferServiceTestData.Agent("Newcomer", arguments: "{NOPE}"), SettingsTransferServiceTestData.Agent("Other"), SettingsTransferServiceTestData.Agent("other")],
			[GateTestInput.Create("Build")],
			null,
			new SandboxSettingsInput("FROM ubuntu"));

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);

		Assert.Equal(
			[
				"Agent 'Newcomer': Arguments use {NOPE}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.",
				"The file has more than one agent named 'Other'.",
			],
			Find(preview, TransferSection.Agents).Problems);
		AssertChanges(preview, TransferSection.Agents, added: [], changed: [], removed: [], reordered: false);
		Assert.Equal(["The Dockerfile must start FROM ${PYXIE_BASE}, Pyxie's base image."], Find(preview, TransferSection.Sandbox).Problems);
		AssertChanges(preview, TransferSection.Sandbox, added: [], changed: [], removed: [], reordered: false);
		Assert.True(Find(preview, TransferSection.Gates).CanImport, "A rule broken in one section does not stop another.");
		AssertChanges(preview, TransferSection.Gates, added: ["Build"], changed: [], removed: [], reordered: false);
	}

	[Fact]
	public async Task PreviewAsync_ListsOnlyTheSectionsTheFileHolds()
	{
		await using TestDatabase database = new();
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, new ProjectSettings(null, [GateTestInput.Create("Build")], null, new SandboxSettingsInput(SettingsTransferServiceTestData.DefaultDockerfile)));

		Assert.Equal([TransferSection.Gates, TransferSection.Sandbox], preview.Sections.Select(section => section.Section));
		Assert.Null(preview.Find(TransferSection.Agents));
		Assert.Null(preview.Find(TransferSection.Categories));
	}

	[Fact]
	public async Task PreviewAsync_OfAnEmptySection_ListsEveryItemOfItAsRemoved()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Test");
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, new ProjectSettings(null, [], [], null));

		AssertChanges(preview, TransferSection.Gates, added: [], changed: [], removed: ["Build", "Test"], reordered: false);
		AssertChanges(preview, TransferSection.Categories, added: [], changed: [], removed: ["Feature", "Bug", "Chore"], reordered: false);
	}

	[Fact]
	public async Task PreviewAsync_WritesNothingEvenWhereTheImportWouldReleaseFinishedCards()
	{
		await using TestDatabase database = new();
		Agent finished = await TestData.AddAgentAsync(database, "Finished", canWork: true, canReview: true);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: true, canReview: true);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, finished.Id, reviewer.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.FeatureCategoryId, finished.Id, reviewer.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new(
			[AgentInput.From(reviewer) with { CanReview = false }],
			[GateTestInput.Create("Build")],
			[.. SettingsTransferServiceTestData.SeededCategories(), SettingsTransferServiceTestData.Category("Research", "#123456")],
			new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();

		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);

		Assert.True(preview.Sections.All(section => section.CanImport), "Finished cards do not stop an import; it releases them.");
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
		await CardAssert.UnchangedAsync(database, complete, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, closed, expectedCardCount: 2);
	}

	private static SectionPreview Find(SettingsImportPreview preview, TransferSection section)
	{
		SectionPreview? found = preview.Find(section);
		Assert.True(found is not null, $"The preview lists every section the file holds, {section} among them.");

		return found;
	}

	private static void AssertChanges(SettingsImportPreview preview, TransferSection section, string[] added, string[] changed, string[] removed, bool reordered)
	{
		SectionChanges changes = Find(preview, section).Changes;
		Assert.Equal(section, changes.Section);
		Assert.Equal(added, changes.Added);
		Assert.Equal(changed, changes.Changed);
		Assert.Equal(removed, changes.Removed);
		Assert.True(changes.Reordered == reordered, $"The {section} preview should {(reordered ? string.Empty : "not ")}report a reorder.");
	}
}
