using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class AgentServiceTests
{
	private static AgentInput Input(string name, bool canWork = true, bool canReview = true, HarnessKind harness = HarnessKind.ClaudeCode, string command = "claude", string arguments = "")
	{
		return new AgentInput(name, harness, command, arguments, canWork, canReview);
	}

	[Fact]
	public async Task ListAsync_ReturnsAgentsOrderedByName()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Zed", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Alpha", canWork: true, canReview: false);
		await TestData.AddAgentAsync(database, "Mid", canWork: false, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<Agent> agents = await service.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Alpha", "Mid", "Zed"], agents.Select(agent => agent.Name));
	}

	[Fact]
	public async Task ListAsync_IsEmptyWhenNoAgentsExist()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		Assert.Empty(await service.ListAsync(TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode)]
	[InlineData(HarnessKind.Codex)]
	[InlineData(HarnessKind.OpenCode)]
	[InlineData(HarnessKind.Other)]
	public async Task CreateAsync_PersistsEveryField(HarnessKind harness)
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent created = ResultAssert.Succeeded(await service.CreateAsync(Input("Runner", canWork: true, canReview: false, harness: harness, command: "run.exe", arguments: "--fast -v"), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, created.Id);
		Assert.NotNull(stored);
		Assert.Equal("Runner", stored.Name);
		Assert.Equal(harness, stored.Harness);
		Assert.Equal("run.exe", stored.Command);
		Assert.Equal("--fast -v", stored.Arguments);
		Assert.True(stored.CanWork, "CanWork should be stored.");
		Assert.False(stored.CanReview, "CanReview should be stored.");
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public async Task CreateAsync_StoresRoleFlags(bool canWork, bool canReview)
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent created = ResultAssert.Succeeded(await service.CreateAsync(Input("Agent", canWork, canReview), TestContext.Current.CancellationToken));

		Assert.Equal(canWork, created.CanWork);
		Assert.Equal(canReview, created.CanReview);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public async Task CreateAsync_RejectsBlankName(string name)
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.CreateAsync(Input(name), TestContext.Current.CancellationToken));

		Assert.Empty(await service.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CreateAsync_RejectsNameLongerThanLimit()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.CreateAsync(Input(new string('a', PyxieLimits.MaxNameLength + 1)), token));
		ResultAssert.Succeeded(await service.CreateAsync(Input(new string('a', PyxieLimits.MaxNameLength)), token));

		Assert.Single(await service.ListAsync(token));
	}

	[Fact]
	public async Task CreateAsync_RejectsCommandLongerThanLimit()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.CreateAsync(Input("Long", command: new string('c', PyxieLimits.MaxCommandLength + 1)), token));
		ResultAssert.Succeeded(await service.CreateAsync(Input("Fits", command: new string('c', PyxieLimits.MaxCommandLength)), token));

		Assert.Single(await service.ListAsync(token));
	}

	[Fact]
	public async Task CreateAsync_RejectsArgumentsLongerThanLimit()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.CreateAsync(Input("Long", arguments: new string('a', PyxieLimits.MaxArgumentsLength + 1)), token));
		ResultAssert.Succeeded(await service.CreateAsync(Input("Fits", arguments: new string('a', PyxieLimits.MaxArgumentsLength)), token));

		Assert.Single(await service.ListAsync(token));
	}

	[Theory]
	[InlineData("Claude")]
	[InlineData("claude")]
	[InlineData("CLAUDE")]
	public async Task CreateAsync_RejectsDuplicateNameIgnoringCase(string name)
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.CreateAsync(Input(name), TestContext.Current.CancellationToken));

		Assert.Single(await service.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task UpdateAsync_ChangesEveryField()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Old", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent updated = ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("New", canWork: false, canReview: true, harness: HarnessKind.Codex, command: "codex", arguments: "--x"), TestContext.Current.CancellationToken));

		Assert.Equal(agent.Id, updated.Id);
		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.Equal("New", stored.Name);
		Assert.Equal(HarnessKind.Codex, stored.Harness);
		Assert.Equal("codex", stored.Command);
		Assert.Equal("--x", stored.Arguments);
		Assert.False(stored.CanWork, "CanWork should be cleared.");
		Assert.True(stored.CanReview, "CanReview should be kept.");
	}

	[Theory]
	[InlineData("claude")]
	[InlineData("CLAUDE")]
	[InlineData("Claude")]
	public async Task UpdateAsync_AllowsKeepingOwnNameIgnoringCase(string name)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input(name), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.Equal(name, stored.Name);
	}

	[Theory]
	[InlineData("Codex")]
	[InlineData("codex")]
	public async Task UpdateAsync_RejectsNameUsedByAnotherAgent(string name)
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.UpdateAsync(claude.Id, Input(name), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, claude.Id);
		Assert.NotNull(stored);
		Assert.Equal("Claude", stored.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	public async Task UpdateAsync_RejectsBlankName(string name)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input(name), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task UpdateAsync_RejectsUnknownAgent()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.UpdateAsync(999, Input("Ghost"), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingCanWorkWhileTheAgentIsTheWorkerOfCardsThatAreNotComplete()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "One", CardLane.InProgress, workerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Two", CardLane.InProgress, workerAgentId: agent.Id);
		WorkCard queued = await TestData.AddCardAsync(database, "Idle", workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input("Agent", canWork: false, canReview: true), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is the worker of 3 cards that are not complete and must keep the worker role.", message);
		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.True(stored.CanWork, "The refused change must not be applied.");
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 3);
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingCanReviewWhileTheAgentIsTheReviewerOfACardThatIsNotComplete()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "One", CardLane.Review, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input("Agent", canWork: true, canReview: false), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is the reviewer of 1 card that is not complete and must keep the reviewer role.", message);
		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.True(stored.CanReview, "The refused change must not be applied.");
	}

	[Fact]
	public async Task UpdateAsync_RemovingCanWorkClearsTheWorkerOfCompleteAndClosedCardsOnly()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, workerAgentId: agent.Id);
		WorkCard unrelated = await TestData.AddCardAsync(database, "Unrelated");
		AgentService service = new(database.ContextFactory, database.Clock);
		DateTime releasedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Agent", canWork: false, canReview: true), TestContext.Current.CancellationToken));

		foreach (WorkCard original in new[] { complete, closed })
		{
			WorkCard? card = await TestData.FindCardAsync(database, original.Id);
			Assert.NotNull(card);
			Assert.Null(card.WorkerAgentId);
			Assert.Equal(releasedAt, card.UpdatedUtc);
			Assert.Equal(original.Version + 1, card.Version);
		}

		WorkCard? kept = await TestData.FindCardAsync(database, complete.Id);
		Assert.NotNull(kept);
		Assert.Equal(agent.Id, kept.ReviewerAgentId);
		await CardAssert.UnchangedAsync(database, unrelated, expectedCardCount: 3);
	}

	[Fact]
	public async Task UpdateAsync_RemovingCanReviewClearsTheReviewerOfCompleteAndClosedCardsOnly()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);
		DateTime releasedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Agent", canWork: true, canReview: false), TestContext.Current.CancellationToken));

		WorkCard? first = await TestData.FindCardAsync(database, complete.Id);
		WorkCard? second = await TestData.FindCardAsync(database, closed.Id);
		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.Null(first.ReviewerAgentId);
		Assert.Equal(agent.Id, first.WorkerAgentId);
		Assert.Null(second.ReviewerAgentId);
		Assert.Equal(releasedAt, first.UpdatedUtc);
		Assert.Equal(releasedAt, second.UpdatedUtc);
	}

	[Fact]
	public async Task UpdateAsync_AllowsRemovingCanWorkWhenOnlyUsedAsReviewer()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "One", reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Agent", canWork: false, canReview: true), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.False(stored.CanWork, "CanWork should be cleared.");
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_AllowsRemovingCanReviewWhenOnlyUsedAsWorker()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "One", workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Agent", canWork: true, canReview: false), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.False(stored.CanReview, "CanReview should be cleared.");
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_AllowsRemovingRolesOfUnusedAgent()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Agent", canWork: false, canReview: false), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.False(stored.CanWork, "CanWork should be cleared.");
		Assert.False(stored.CanReview, "CanReview should be cleared.");
	}

	[Fact]
	public async Task UpdateAsync_AllowsRenamingAgentThatIsInUse()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "One", workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Renamed", harness: HarnessKind.OpenCode, command: "opencode"), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.Equal("Renamed", stored.Name);
		Assert.Equal(HarnessKind.OpenCode, stored.Harness);
	}

	[Fact]
	public async Task DeleteAsync_RemovesUnusedAgent()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindAgentAsync(database, agent.Id));
	}

	[Fact]
	public async Task DeleteAsync_ClearsTheAgentFromCompleteAndClosedCardsAndStampsThem()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard completeBoth = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard completeReviewer = await TestData.AddCardAsync(database, "Complete reviewed", CardLane.Complete, reviewerAgentId: agent.Id);
		WorkCard closedWorker = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, workerAgentId: agent.Id);
		WorkCard unrelated = await TestData.AddCardAsync(database, "Unrelated");
		AgentService service = new(database.ContextFactory, database.Clock);
		DateTime deletedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindAgentAsync(database, agent.Id));
		Assert.Equal(4, await TestData.CountCardsAsync(database));
		foreach (WorkCard original in new[] { completeBoth, completeReviewer, closedWorker })
		{
			WorkCard? card = await TestData.FindCardAsync(database, original.Id);
			Assert.NotNull(card);
			Assert.NotEqual(agent.Id, card.WorkerAgentId);
			Assert.Null(card.ReviewerAgentId);
			Assert.Equal(deletedAt, card.UpdatedUtc);
			Assert.Equal(original.Version + 1, card.Version);
		}

		Assert.Null((await TestData.FindCardAsync(database, completeBoth.Id))?.WorkerAgentId);
		Assert.Null((await TestData.FindCardAsync(database, closedWorker.Id))?.WorkerAgentId);
		await CardAssert.UnchangedAsync(database, unrelated, expectedCardCount: 4);
	}

	[Fact]
	public async Task DeleteAsync_AllowsAgentReferencedOnlyByClosedCards_AndClearsThemSoHistoryLosesWhoDidTheWork()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard finished = await TestData.AddCardAsync(database, "Finished", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		CardService cards = new(database.ContextFactory, database.Clock);
		AgentService agents = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await cards.CloseAsync(finished.Id, token));

		ResultAssert.Succeeded(await agents.DeleteAsync(agent.Id, token));

		WorkCard? closed = await TestData.FindCardAsync(database, finished.Id);
		Assert.NotNull(closed);
		Assert.Equal(CardLane.Closed, closed.Lane);
		Assert.Null(closed.WorkerAgentId);
		Assert.Null(closed.ReviewerAgentId);
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentThatIsTheWorkerOfCardsThatAreNotCompleteAndReportsTheCount()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "One", CardLane.InProgress, workerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Two", CardLane.InProgress, workerAgentId: agent.Id);
		WorkCard idle = await TestData.AddCardAsync(database, "Idle", CardLane.Queue, workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is assigned to 3 cards that are not complete (3 as worker) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(database, agent.Id));
		await CardAssert.UnchangedAsync(database, idle, expectedCardCount: 3);
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentThatIsTheReviewerOfCardsThatAreNotCompleteAndReportsTheCount()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "One", CardLane.Review, reviewerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Two", CardLane.Review, reviewerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Three", CardLane.Gates, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is assigned to 3 cards that are not complete (3 as reviewer) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(database, agent.Id));
	}

	[Fact]
	public async Task DeleteAsync_NamesBothRolesWhenTheAgentWorksOneCardAndReviewsAnother()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "One", CardLane.InProgress, workerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Two", CardLane.Review, reviewerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Three", CardLane.Queue, workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is assigned to 3 cards that are not complete (2 as worker, 1 as reviewer) and cannot be deleted.", message);
	}

	[Fact]
	public async Task DeleteAsync_AllowsAgentAfterItsCardsAreComplete()
	{
		await using PipelineRig rig = new();
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: true);
		WorkCard card = await rig.CreateCardAsync("One", agent.Id);
		CancellationToken token = TestContext.Current.CancellationToken;
		PipelineWork work = await rig.ClaimAsync();
		ResultAssert.Failed(await rig.Agents.DeleteAsync(agent.Id, token));

		await rig.CompleteAsync(work);
		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(agent.Id, token));

		Assert.Null(await TestData.FindAgentAsync(rig.Database, agent.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Complete, stored.Lane);
		Assert.Null(stored.WorkerAgentId);
		Assert.Equal("Agent", (await TestData.ReadRoundsAsync(rig.Database, card.Id))[0].Agent?.Name);
	}

	[Fact]
	public async Task DeleteAsync_RejectsUnknownAgent()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.DeleteAsync(999, TestContext.Current.CancellationToken));
	}
}
