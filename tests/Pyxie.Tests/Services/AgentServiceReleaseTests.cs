using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A refused delete or role change rolls back everything it released, and every finished card that loses a reference advances its version exactly once.</summary>
public sealed class AgentServiceReleaseTests
{
	private static AgentInput Input(bool canWork, bool canReview)
	{
		return new AgentInput("Agent", HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview);
	}

	[Fact]
	public async Task DeleteAsync_WhenRefused_LeavesTheAgentOnEveryFinishedCardWithItsVersionAndTimestampUnchanged()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard working = await TestData.AddCardAsync(database, "Working", CardLane.InProgress, workerAgentId: agent.Id);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, reviewerAgentId: agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);
		database.Clock.Tick();

		ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.NotNull(await TestData.FindAgentAsync(database, agent.Id));
		await CardAssert.UnchangedAsync(database, working, expectedCardCount: 4);
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 4);
		await CardAssert.UnchangedAsync(database, complete, expectedCardCount: 4);
		await CardAssert.UnchangedAsync(database, closed, expectedCardCount: 4);
	}

	[Fact]
	public async Task UpdateAsync_WhenRefused_LeavesTheAgentAndEveryFinishedCardUnchanged()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard working = await TestData.AddCardAsync(database, "Working", CardLane.InProgress, workerAgentId: agent.Id);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", workerAgentId: agent.Id);
		WorkCard complete = await TestData.AddCardAsync(database, "Complete", CardLane.Complete, workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);
		database.Clock.Tick();

		ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input(canWork: false, canReview: true), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.True(stored.CanWork, "The refused change must not be applied.");
		await CardAssert.UnchangedAsync(database, working, expectedCardCount: 3);
		await CardAssert.UnchangedAsync(database, queued, expectedCardCount: 3);
		await CardAssert.UnchangedAsync(database, complete, expectedCardCount: 3);
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentWhoIsBothWorkerAndReviewerOfACardThatIsNotCompleteAndClearsNothing()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard both = await TestData.AddCardAsync(database, "Both", CardLane.InProgress, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard finished = await TestData.AddCardAsync(database, "Finished", CardLane.Complete, workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);
		database.Clock.Tick();

		string message = ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		// One card holding the agent in both roles is one card, not two.
		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as worker, 1 as reviewer) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(database, agent.Id));
		await CardAssert.UnchangedAsync(database, both, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, finished, expectedCardCount: 2);
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingBothRolesFromAnAgentWhoIsWorkingACardAndClearsNothing()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard both = await TestData.AddCardAsync(database, "Both", CardLane.InProgress, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard finished = await TestData.AddCardAsync(database, "Finished", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);
		database.Clock.Tick();

		ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.True(stored.CanWork && stored.CanReview, "The refused change must not be applied.");
		await CardAssert.UnchangedAsync(database, both, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, finished, expectedCardCount: 2);
	}

	[Fact]
	public async Task UpdateAsync_RemovingBothRolesAdvancesEachReleasedCardsVersionExactlyOnceAndStampsItAndSparesTheRest()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard bothComplete = await TestData.AddCardAsync(database, "BothComplete", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard workerClosed = await TestData.AddCardAsync(database, "WorkerClosed", CardLane.Closed, workerAgentId: agent.Id);
		WorkCard reviewerComplete = await TestData.AddCardAsync(database, "ReviewerComplete", CardLane.Complete, reviewerAgentId: agent.Id);
		WorkCard unrelatedQueued = await TestData.AddCardAsync(database, "UnrelatedQueued");
		WorkCard unrelatedComplete = await TestData.AddCardAsync(database, "UnrelatedComplete", CardLane.Complete);
		AgentService service = new(database.ContextFactory, database.Clock);
		DateTime releasedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		foreach (WorkCard original in new[] { bothComplete, workerClosed, reviewerComplete })
		{
			WorkCard? stored = await TestData.FindCardAsync(database, original.Id);
			Assert.NotNull(stored);
			Assert.Equal(original.Version + 1, stored.Version);
			Assert.Equal(releasedAt, stored.UpdatedUtc);
			Assert.NotEqual(agent.Id, stored.WorkerAgentId);
			Assert.Null(stored.ReviewerAgentId);
		}

		Assert.Null((await TestData.FindCardAsync(database, bothComplete.Id))?.WorkerAgentId);
		Assert.Null((await TestData.FindCardAsync(database, workerClosed.Id))?.WorkerAgentId);
		await CardAssert.UnchangedAsync(database, unrelatedQueued, expectedCardCount: 5);
		await CardAssert.UnchangedAsync(database, unrelatedComplete, expectedCardCount: 5);
	}
}
