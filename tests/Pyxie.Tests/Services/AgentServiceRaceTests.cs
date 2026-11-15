using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A person can assign an agent to a card between the moment the agent service reads the agent and the moment it releases and counts the cards that name it. The service must then refuse, so no card that is not complete ever loses an agent it was given. The assignment lands either before the service's transaction begins or inside it, before the service's first write. An assignment simulated inside the transaction (a raw statement on the service's own connection) rolls back with the refusal, so those tests check the refusal and the agent, not the assignment's effects.</summary>
public sealed class AgentServiceRaceTests
{
	private static AgentInput Input(bool canWork, bool canReview)
	{
		return new AgentInput("Agent", HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview);
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentThatAPersonAssignsAsWorkerAfterTheAgentWasRead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: false);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput assign = TestCardInput.Create("Card", workerAgentId: agent.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, assign, TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(rig.Database, agent.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(agent.Id, stored.WorkerAgentId);
		Assert.Equal(card.Version + 1, stored.Version);
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentThatAPersonAssignsAsReviewerAfterTheAgentWasRead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: false, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput assign = await TestCardInput.CreateAsync(rig.Database, "Card", reviewerAgentId: agent.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, assign, TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as reviewer) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(rig.Database, agent.Id));
		Assert.Equal(agent.Id, (await TestData.FindCardAsync(rig.Database, card.Id))?.ReviewerAgentId);
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingCanWorkFromAnAgentThatAPersonAssignsAsWorkerAfterTheAgentWasRead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: false);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput assign = TestCardInput.Create("Card", workerAgentId: agent.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, assign, TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is the worker of 1 card that is not complete and must keep the worker role.", message);
		Assert.True((await TestData.FindAgentAsync(rig.Database, agent.Id))?.CanWork, "The refused change must not be applied.");
		Assert.Equal(agent.Id, (await TestData.FindCardAsync(rig.Database, card.Id))?.WorkerAgentId);
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingCanReviewFromAnAgentThatAPersonAssignsAsReviewerAfterTheAgentWasRead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: false, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput assign = await TestCardInput.CreateAsync(rig.Database, "Card", reviewerAgentId: agent.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, assign, TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is the reviewer of 1 card that is not complete and must keep the reviewer role.", message);
		Assert.True((await TestData.FindAgentAsync(rig.Database, agent.Id))?.CanReview, "The refused change must not be applied.");
		Assert.Equal(agent.Id, (await TestData.FindCardAsync(rig.Database, card.Id))?.ReviewerAgentId);
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentAssignedAsWorkerInsideTheTransactionBeforeTheFirstWrite()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: false);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		interceptor.Arm($"UPDATE \"Cards\" SET \"WorkerAgentId\" = {agent.Id}, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(rig.Database, agent.Id));
	}

	[Fact]
	public async Task DeleteAsync_RefusesAnAgentAssignedAsReviewerInsideTheTransactionBeforeTheFirstWrite()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: false, canReview: true);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", CardLane.Gates);
		interceptor.Arm($"UPDATE \"Cards\" SET \"ReviewerAgentId\" = {agent.Id}, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is assigned to 1 card that is not complete (1 as reviewer) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(rig.Database, agent.Id));
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingCanWorkWhenTheAgentIsAssignedInsideTheTransactionBeforeTheFirstWrite()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: false);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		interceptor.Arm($"UPDATE \"Cards\" SET \"WorkerAgentId\" = {agent.Id}, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is the worker of 1 card that is not complete and must keep the worker role.", message);
		Assert.True((await TestData.FindAgentAsync(rig.Database, agent.Id))?.CanWork, "The refused change must not be applied.");
	}

	[Fact]
	public async Task UpdateAsync_RefusesRemovingCanReviewWhenTheAgentIsAssignedInsideTheTransactionBeforeTheFirstWrite()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: false, canReview: true);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", CardLane.Review);
		interceptor.Arm($"UPDATE \"Cards\" SET \"ReviewerAgentId\" = {agent.Id}, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(agent.Id, Input(canWork: false, canReview: false), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Agent' is the reviewer of 1 card that is not complete and must keep the reviewer role.", message);
		Assert.True((await TestData.FindAgentAsync(rig.Database, agent.Id))?.CanReview, "The refused change must not be applied.");
	}

	[Fact]
	public async Task DeleteAsync_StillDeletesAnAgentWhoseOnlyCardsAreFinished_WhileAPipelineClaimRunsAfterTheRead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: false);
		WorkCard finished = await TestData.AddCardAsync(rig.Database, "Finished", CardLane.Complete, workerAgentId: agent.Id);
		WorkCard queued = await TestData.AddCardAsync(rig.Database, "Queued");
		interceptor.Arm(async () => await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Null(await TestData.FindAgentAsync(rig.Database, agent.Id));
		Assert.Null((await TestData.FindCardAsync(rig.Database, finished.Id))?.WorkerAgentId);
		Assert.Equal(CardLane.InProgress, (await TestData.FindCardAsync(rig.Database, queued.Id))?.Lane);
	}
}
