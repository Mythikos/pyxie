using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Which lanes hold an agent to its role: an agent cannot be deleted or lose a role while it is assigned in that role to a card that is not finished (in the Queue, In Progress, Gates, or Review), and the agent is released from cards that are (Complete and Closed).</summary>
public sealed class AgentServiceActiveLaneTests
{
	private static AgentInput Input(bool canWork, bool canReview)
	{
		return new AgentInput("Agent", HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task DeleteAsync_RefusesAWorkerWhoseCardIsNotFinishedWhicheverLaneItIsIn(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: false);
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, workerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(database, agent.Id));
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task DeleteAsync_RefusesAReviewerWhoseCardIsNotFinishedWhicheverLaneItIsIn(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: false, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as reviewer) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(database, agent.Id));
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(CardLane.Complete)]
	[InlineData(CardLane.Closed)]
	public async Task DeleteAsync_AllowsAWorkerAndAReviewerWhoseCardsAreFinishedAndClearsThem(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard worked = await TestData.AddCardAsync(database, "Worked", lane, workerAgentId: agent.Id);
		WorkCard reviewed = await TestData.AddCardAsync(database, "Reviewed", lane, workerAgentId: null, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Null((await TestData.FindCardAsync(database, worked.Id))?.WorkerAgentId);
		Assert.Null((await TestData.FindCardAsync(database, reviewed.Id))?.ReviewerAgentId);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task UpdateAsync_RefusesRemovingTheWorkerRoleWhileTheCardIsNotFinishedWhicheverLaneItIsIn(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input(canWork: false, canReview: true), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is the worker of 1 card that is not complete and must keep the worker role.", message);
		Assert.True((await TestData.FindAgentAsync(database, agent.Id))?.CanWork, "The refused change must not be applied.");
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task UpdateAsync_RefusesRemovingTheReviewerRoleWhileTheCardIsNotFinishedWhicheverLaneItIsIn(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input(canWork: true, canReview: false), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is the reviewer of 1 card that is not complete and must keep the reviewer role.", message);
		Assert.True((await TestData.FindAgentAsync(database, agent.Id))?.CanReview, "The refused change must not be applied.");
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_ReportsTheWorkerRoleBeforeTheReviewerRoleWhenBothAreRefused()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Worked", CardLane.InProgress, workerAgentId: agent.Id);
		await TestData.AddCardAsync(database, "Reviewed", CardLane.Review, reviewerAgentId: agent.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' is the worker of 1 card that is not complete and must keep the worker role.", message);
	}

	[Theory]
	[InlineData(CardLane.Complete)]
	[InlineData(CardLane.Closed)]
	public async Task UpdateAsync_RemovingARoleReleasesTheAgentFromFinishedCardsAndAdvancesTheirVersionOnce(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard bystander = await TestData.AddCardAsync(database, "Bystander", lane);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input(canWork: false, canReview: true), TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Null(stored.WorkerAgentId);
		Assert.Equal(agent.Id, stored.ReviewerAgentId);
		Assert.Equal(card.Version + 1, stored.Version);
		await CardAssert.UnchangedAsync(database, bystander, expectedCardCount: 2);
	}

	[Fact]
	public async Task DeleteAsync_ClearsTheReferencesAndAdvancesTheVersionOfEveryFinishedCardThatNamedTheAgent()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard both = await TestData.AddCardAsync(database, "Both", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed, workerAgentId: agent.Id);
		WorkCard unrelated = await TestData.AddCardAsync(database, "Unrelated", CardLane.Complete);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		WorkCard? bothAfter = await TestData.FindCardAsync(database, both.Id);
		WorkCard? closedAfter = await TestData.FindCardAsync(database, closed.Id);
		Assert.NotNull(bothAfter);
		Assert.NotNull(closedAfter);
		// A card that lost both agents in one delete advances its version exactly once.
		Assert.Equal(both.Version + 1, bothAfter.Version);
		Assert.Equal(closed.Version + 1, closedAfter.Version);
		Assert.Null(bothAfter.WorkerAgentId);
		Assert.Null(bothAfter.ReviewerAgentId);
		Assert.Null(closedAfter.WorkerAgentId);
		await CardAssert.UnchangedAsync(database, unrelated, expectedCardCount: 3);
	}

	[Fact]
	public async Task DeleteAsync_AllowsAnAgentWhoseRoundsAreHistoryAndKeepsTheRounds()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.Complete, workerAgentId: agent.Id, reviewerAgentId: agent.Id);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Agent");
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));
		Assert.Equal("Agent", round.Agent?.Name);
	}
}
