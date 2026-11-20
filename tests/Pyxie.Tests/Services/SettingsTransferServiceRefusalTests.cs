using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An import is all or nothing under the editors' rules. Every case here picks all four sections of a file that changes every one of them, and is refused with nothing changed at all: an agent that unfinished cards need, a category any card uses, or a value the rules refuse stops the whole import, in the editors' own words, one reason per line after the lead; and what the import released on finished cards before it found the reason is rolled back.</summary>
public sealed class SettingsTransferServiceRefusalTests
{
	private const string Lead = "Nothing was imported.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task ImportAsync_RemovingTheWorkerOfAnUnfinishedCard_IsRefusedInTheAgentEditorsWords(CardLane lane)
	{
		await using TestDatabase database = new();
		(Agent worker, _, _) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Card", lane, PyxieSeedData.FeatureCategoryId, worker.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Worker" ? null : agent);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\n{await AgentServiceDeleteRefusalAsync("Worker", lane, asWorker: true, asReviewer: false)}", error);
		Assert.Equal($"{Lead}\nAgent 'Worker' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", error);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task ImportAsync_RemovingTheReviewerOfAnUnfinishedCard_IsRefusedInTheAgentEditorsWords(CardLane lane)
	{
		await using TestDatabase database = new();
		(Agent worker, Agent reviewer, _) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Card", lane, PyxieSeedData.FeatureCategoryId, worker.Id, reviewer.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Reviewer" ? null : agent);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\n{await AgentServiceDeleteRefusalAsync("Reviewer", lane, asWorker: false, asReviewer: true)}", error);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.Review)]
	public async Task ImportAsync_TakingTheWorkerRoleFromTheWorkerOfAnUnfinishedCard_IsRefusedInTheAgentEditorsWords(CardLane lane)
	{
		await using TestDatabase database = new();
		(_, _, Agent spare) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Card", lane, PyxieSeedData.FeatureCategoryId, spare.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Spare" ? agent with { CanWork = false } : agent);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\n{await AgentServiceRoleRefusalAsync("Spare", lane, dropWorker: true)}", error);
		Assert.Equal($"{Lead}\nAgent 'Spare' is the worker of 1 card that is not complete and must keep the worker role.", error);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.Gates)]
	public async Task ImportAsync_TakingTheReviewerRoleFromTheReviewerOfAnUnfinishedCard_IsRefusedInTheAgentEditorsWords(CardLane lane)
	{
		await using TestDatabase database = new();
		(Agent worker, _, Agent spare) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Card", lane, PyxieSeedData.FeatureCategoryId, worker.Id, spare.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Spare" ? agent with { CanReview = false } : agent);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\n{await AgentServiceRoleRefusalAsync("Spare", lane, dropWorker: false)}", error);
		Assert.Equal($"{Lead}\nAgent 'Spare' is the reviewer of 1 card that is not complete and must keep the reviewer role.", error);
	}

	[Fact]
	public async Task ImportAsync_RenamingAnAgentThatAnUnfinishedCardUses_IsRefusedAsItsRemoval()
	{
		await using TestDatabase database = new();
		(Agent worker, _, _) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "First", CardLane.Queue, PyxieSeedData.FeatureCategoryId, worker.Id);
		await TestData.AddCardAsync(database, "Second", CardLane.InProgress, PyxieSeedData.FeatureCategoryId, worker.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Worker" ? agent with { Name = "Builder" } : agent);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\nAgent 'Worker' is assigned to 2 cards that are not complete (2 as worker) and cannot be deleted.", error);
	}

	[Fact]
	public async Task ImportAsync_RemovingACategoryThatOnlyAClosedCardUses_IsRefusedInTheCategoryEditorsWords()
	{
		await using TestDatabase database = new();
		(Agent worker, _, _) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.ChoreCategoryId, worker.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent, category => category.Name == "Chore" ? null : category);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\n{await CategoryServiceDeleteRefusalAsync(PyxieSeedData.ChoreCategoryId, CardLane.Closed)}", error);
		Assert.Equal($"{Lead}\nCategory 'Chore' is used by 1 card and cannot be deleted.", error);
	}

	[Fact]
	public async Task ImportAsync_WithSeveralReasons_ListsEveryOneOnItsOwnLineAfterTheLead()
	{
		await using TestDatabase database = new();
		(Agent worker, Agent reviewer, Agent spare) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.BugCategoryId, worker.Id, reviewer.Id);
		await TestData.AddCardAsync(database, "Working", CardLane.InProgress, PyxieSeedData.BugCategoryId, spare.Id);
		await TestData.AddCardAsync(database, "Gated", CardLane.Gates, PyxieSeedData.FeatureCategoryId, worker.Id, spare.Id);
		await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.ChoreCategoryId, worker.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(
			service,
			agent => agent.Name switch
			{
				"Worker" => null,
				"Reviewer" => agent with { CanReview = false },
				"Spare" => agent with { CanWork = false, CanReview = false },
				_ => agent,
			},
			category => category.Name is "Bug" or "Chore" ? null : category);

		string error = await ImportRefusedAsync(database, service, settings);

		// Removed agents first, then the agents that lose a role in the file's order, the worker role before the reviewer role, then removed categories.
		Assert.Equal(
			[
				Lead,
				"Agent 'Worker' is assigned to 2 cards that are not complete (2 as worker) and cannot be deleted.",
				"Agent 'Reviewer' is the reviewer of 1 card that is not complete and must keep the reviewer role.",
				"Agent 'Spare' is the worker of 1 card that is not complete and must keep the worker role.",
				"Agent 'Spare' is the reviewer of 1 card that is not complete and must keep the reviewer role.",
				"Category 'Bug' is used by 2 cards and cannot be deleted.",
				"Category 'Chore' is used by 1 card and cannot be deleted.",
			],
			error.Split('\n'));
	}

	// The release is each blocked agent's first write, so whichever agent is found first, what was released for the other is rolled back with it.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ImportAsync_WhenRefused_RollsBackWhatItReleasedOnFinishedCards(bool releasedAgentFirst)
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		Agent finished;
		Agent busy;
		if (releasedAgentFirst)
		{
			finished = await TestData.AddAgentAsync(database, "Finished", canWork: true, canReview: true);
			busy = await TestData.AddAgentAsync(database, "Busy", canWork: true, canReview: true);
		}
		else
		{
			busy = await TestData.AddAgentAsync(database, "Busy", canWork: true, canReview: true);
			finished = await TestData.AddAgentAsync(database, "Finished", canWork: true, canReview: true);
		}

		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, finished.Id, finished.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, PyxieSeedData.FeatureCategoryId, workerAgentId: null, reviewerAgentId: finished.Id);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, busy.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name is "Finished" or "Busy" ? null : agent);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\nAgent 'Busy' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", error);
		await CardAssert.UnchangedAsync(database, complete, expectedCardCount: 3);
		await CardAssert.UnchangedAsync(database, closed, expectedCardCount: 3);
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 3);
	}

	[Fact]
	public async Task ImportAsync_WhenRefused_RollsBackTheReleaseOfARoleTakenFromAnAgent()
	{
		await using TestDatabase database = new();
		(Agent worker, _, Agent spare) = await ArrangeAsync(database);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, spare.Id, spare.Id);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", CardLane.Queue, PyxieSeedData.FeatureCategoryId, worker.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name switch
		{
			"Spare" => agent with { CanReview = false },
			"Worker" => null,
			_ => agent,
		});

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\nAgent 'Worker' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", error);
		await CardAssert.UnchangedAsync(database, complete, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 2);
	}

	[Theory]
	[InlineData("unknown tokens")]
	[InlineData("agent name too long")]
	[InlineData("blank agent name")]
	[InlineData("unknown harness")]
	[InlineData("agent command too long")]
	[InlineData("duplicate agent names")]
	[InlineData("two merge agents")]
	[InlineData("timeout too short")]
	[InlineData("timeout too long")]
	[InlineData("blank gate command")]
	[InlineData("duplicate gate names")]
	[InlineData("blank category name")]
	[InlineData("duplicate category names")]
	[InlineData("dockerfile not from the base image")]
	[InlineData("blank dockerfile")]
	public async Task ImportAsync_OfAFileThatBreaksARuleInAPickedSection_IsRefusedWithTheProblem(string rule)
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		(ProjectSettings settings, string problem) = Break(await FileAsync(service, agent => agent), rule);

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal($"{Lead}\n{problem}", error);
	}

	[Fact]
	public async Task ImportAsync_OfAFileThatBreaksRulesInSeveralPickedSections_ListsEveryProblemInSectionOrder()
	{
		await using TestDatabase database = new();
		await ArrangeAsync(database);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings file = await FileAsync(service, agent => agent);
		ProjectSettings settings = file with
		{
			Agents = [.. (file.Agents ?? []).Select(agent => agent.Name == "Worker" ? agent with { Arguments = "{NOPE}" } : agent)],
			Gates = [.. (file.Gates ?? []).Select(gate => gate.Name == "Test" ? gate with { TimeoutMinutes = 0 } : gate)],
			Sandbox = new SandboxSettingsInput("FROM ubuntu"),
		};

		string error = await ImportRefusedAsync(database, service, settings);

		Assert.Equal(
			[
				Lead,
				"Agent 'Worker': Arguments use {NOPE}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.",
				"Gate 'Test': Timeout must be between 1 and 240 minutes.",
				"The Dockerfile must start FROM ${PYXIE_BASE}, Pyxie's base image.",
			],
			error.Split('\n'));
	}

	// The release is the transaction's first write and the count follows it, so a card given the agent just before the import's first write is counted.
	[Fact]
	public async Task ImportAsync_RemovingAnAgentThatACardTakesJustBeforeTheImportsFirstWrite_IsRefused()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		(Agent worker, _, _) = await ArrangeAsync(database);
		Agent idle = await TestData.AddAgentAsync(database, "Idle", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.Queue, PyxieSeedData.FeatureCategoryId, worker.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Idle" ? null : agent);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		interceptor.Arm($"UPDATE \"Cards\" SET \"WorkerAgentId\" = {idle.Id}, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {card.Id}");

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		interceptor.AssertFired();
		Assert.True(preview.Sections.All(section => section.CanImport), "Nothing stopped the import when it was previewed.");
		Assert.Equal($"{Lead}\nAgent 'Idle' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", error);
		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	[Fact]
	public async Task ImportAsync_WhenItsSaveFails_RollsBackWhatItReleasedAndRethrows()
	{
		ThrowingSaveInterceptor interceptor = new();
		await using TestDatabase database = new(interceptor);
		(_, _, Agent spare) = await ArrangeAsync(database);
		await TestData.AddCardAsync(database, "Complete", CardLane.Complete, PyxieSeedData.FeatureCategoryId, spare.Id, spare.Id);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = await FileAsync(service, agent => agent.Name == "Spare" ? null : agent);
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();
		interceptor.Arm();

		await Assert.ThrowsAsync<DbUpdateException>(() => service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));
	}

	// A project with a worker-only, a reviewer-only, and an all-round merge agent, two gates, the seeded categories, and saved sandbox settings; each test adds the cards it needs.
	private static async Task<(Agent Worker, Agent Reviewer, Agent Spare)> ArrangeAsync(TestDatabase database)
	{
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true, HarnessKind.Codex, "codex");
		Agent spare = await TestData.AddAgentAsync(database, "Spare", canWork: true, canReview: true, HarnessKind.DeepSeek, "dsh", canMerge: true);
		await GateTestData.AddOrderedAsync(database, "Build", "Test");
		ResultAssert.Succeeded(await new SandboxSettingsService(database.ContextFactory, database.Clock).SaveAsync(new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo stored")), expectedVersion: 0, Token));

		return (worker, reviewer, spare);
	}

	// The project's own export, with each agent and category passed through a change (null removes it), and a change in every section besides, so an import that wrote any part of it would show: an agent, a gate, and a category added, and another Dockerfile.
	private static async Task<ProjectSettings> FileAsync(SettingsTransferService service, Func<AgentInput, AgentInput?> agents, Func<CategoryInput, CategoryInput?>? categories = null)
	{
		ProjectSettings exported = await SettingsTransferServiceTestData.ExportAsync(service);

		return new ProjectSettings(
			[.. (exported.Agents ?? []).Select(agents).OfType<AgentInput>(), SettingsTransferServiceTestData.Agent("Newcomer")],
			[.. exported.Gates ?? [], GateTestInput.Create("Lint", "dotnet", "format")],
			[.. (exported.Categories ?? []).Select(categories ?? (category => category)).OfType<CategoryInput>(), SettingsTransferServiceTestData.Category("Research", "#123456")],
			new SandboxSettingsInput(SettingsTransferServiceTestData.Dockerfile("RUN echo imported")));
	}

	// Previews the file, imports every section of it, and asserts that it was refused with nothing changed; returns the refusal.
	private static async Task<string> ImportRefusedAsync(TestDatabase database, SettingsTransferService service, ProjectSettings settings)
	{
		SettingsImportPreview preview = await SettingsTransferServiceTestData.PreviewAsync(service, settings);
		SettingsTransferServiceState before = await SettingsTransferServiceState.ReadAsync(database);
		database.Clock.Tick();

		string error = ResultAssert.Failed(await service.ImportAsync(settings, preview, SettingsTransferServiceTestData.Every, Token));

		before.AssertUnchanged(await SettingsTransferServiceState.ReadAsync(database));

		return error;
	}

	// The file the rule tests start from lists the agents Reviewer, Spare (the merge agent), Worker, and Newcomer; the gates Build, Test, and Lint; and the categories Bug, Chore, Feature, and Research.
	private static (ProjectSettings Settings, string Problem) Break(ProjectSettings file, string rule)
	{
		List<AgentInput> agents = [.. file.Agents ?? []];
		List<GateInput> gates = [.. file.Gates ?? []];
		List<CategoryInput> categories = [.. file.Categories ?? []];
		string longName = new('W', 101);
		string problem;
		switch (rule)
		{
			case "unknown tokens":
				agents[2] = agents[2] with { Arguments = "--card {CARD} {NOPE} ${HOME}" };
				problem = "Agent 'Worker': Arguments use {CARD} and {NOPE}, which are not tokens Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";
				break;

			case "agent name too long":
				agents[3] = agents[3] with { Name = longName };
				problem = $"Agent '{longName}': Agent name must be 100 characters or fewer.";
				break;

			case "blank agent name":
				agents[3] = agents[3] with { Name = "   " };
				problem = "Agent 4: Agent name is required.";
				break;

			case "unknown harness":
				agents[2] = agents[2] with { Harness = (HarnessKind)99 };
				problem = "Agent 'Worker': Harness is not recognized.";
				break;

			case "agent command too long":
				agents[0] = agents[0] with { Command = new string('c', 501) };
				problem = "Agent 'Reviewer': Command must be 500 characters or fewer.";
				break;

			case "duplicate agent names":
				agents[3] = agents[3] with { Name = "worker" };
				problem = "The file has more than one agent named 'Worker'.";
				break;

			case "two merge agents":
				agents[3] = agents[3] with { CanMerge = true };
				problem = "The file makes 'Spare' and 'Newcomer' merge agents. Only one agent can resolve merge conflicts.";
				break;

			case "timeout too short":
				gates[0] = gates[0] with { TimeoutMinutes = 0 };
				problem = "Gate 'Build': Timeout must be between 1 and 240 minutes.";
				break;

			case "timeout too long":
				gates[2] = gates[2] with { TimeoutMinutes = 241 };
				problem = "Gate 'Lint': Timeout must be between 1 and 240 minutes.";
				break;

			case "blank gate command":
				gates[1] = gates[1] with { Command = " " };
				problem = "Gate 'Test': Command is required.";
				break;

			case "duplicate gate names":
				gates[2] = gates[2] with { Name = "BUILD" };
				problem = "The file has more than one gate named 'Build'.";
				break;

			case "blank category name":
				categories[3] = categories[3] with { Name = string.Empty };
				problem = "Category 4: Category name is required.";
				break;

			case "duplicate category names":
				categories[3] = categories[3] with { Name = " feature " };
				problem = "The file has more than one category named 'Feature'.";
				break;

			case "dockerfile not from the base image":
				return (file with { Sandbox = new SandboxSettingsInput("FROM ubuntu:24.04\nRUN make\n") }, "The Dockerfile must start FROM ${PYXIE_BASE}, Pyxie's base image.");

			case "blank dockerfile":
				return (file with { Sandbox = new SandboxSettingsInput(" \r\n ") }, "Enter the project's Dockerfile.");

			default:
				throw new ArgumentOutOfRangeException(nameof(rule), rule, "No such rule case.");
		}

		return (new ProjectSettings(agents, gates, categories, file.Sandbox), problem);
	}

	private static async Task<string> AgentServiceDeleteRefusalAsync(string name, CardLane lane, bool asWorker, bool asReviewer)
	{
		await using TestDatabase scratch = new();
		Agent agent = await TestData.AddAgentAsync(scratch, name, canWork: true, canReview: true);
		await TestData.AddCardAsync(scratch, "Card", lane, PyxieSeedData.FeatureCategoryId, asWorker ? agent.Id : null, asReviewer ? agent.Id : null);

		return ResultAssert.Failed(await new AgentService(scratch.ContextFactory, scratch.Clock).DeleteAsync(agent.Id, Token));
	}

	private static async Task<string> AgentServiceRoleRefusalAsync(string name, CardLane lane, bool dropWorker)
	{
		await using TestDatabase scratch = new();
		Agent agent = await TestData.AddAgentAsync(scratch, name, canWork: true, canReview: true);
		await TestData.AddCardAsync(scratch, "Card", lane, PyxieSeedData.FeatureCategoryId, dropWorker ? agent.Id : null, dropWorker ? null : agent.Id);

		return ResultAssert.Failed(await new AgentService(scratch.ContextFactory, scratch.Clock).UpdateAsync(agent.Id, AgentInput.From(agent) with { CanWork = !dropWorker, CanReview = dropWorker }, Token));
	}

	private static async Task<string> CategoryServiceDeleteRefusalAsync(int categoryId, CardLane lane)
	{
		await using TestDatabase scratch = new();
		await TestData.AddCardAsync(scratch, "Card", lane, categoryId, workerAgentId: null);

		return ResultAssert.Failed(await new CategoryService(scratch.ContextFactory).DeleteAsync(categoryId, Token));
	}
}
