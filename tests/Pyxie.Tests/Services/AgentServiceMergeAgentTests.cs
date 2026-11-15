using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>At most one agent of a project resolves merge conflicts: a second is refused with a message that names the first, keeping the role, clearing it, and deleting the agent that has it are all allowed, and the role is not a worker or reviewer role.</summary>
public sealed class AgentServiceMergeAgentTests
{
	private const string TakenMessage = "Agent 'Merger' already resolves merge conflicts. Only one agent can.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static AgentInput Input(string name, bool canMerge, bool canWork = true, bool canReview = true)
	{
		return new AgentInput(name, HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview, canMerge);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task CreateAsync_StoresTheMergeFlag(bool canMerge)
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent created = ResultAssert.Succeeded(await service.CreateAsync(Input("Agent", canMerge), Token));

		Assert.Equal(canMerge, created.CanMerge);
		Assert.Equal(canMerge, (await TestData.FindAgentAsync(database, created.Id))?.CanMerge);
	}

	[Fact]
	public async Task CreateAsync_OfAnInputBuiltWithoutTheMergeFlag_IsNotAMergeAgent()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent created = ResultAssert.Succeeded(await service.CreateAsync(new AgentInput("Agent", HarnessKind.ClaudeCode, "claude", string.Empty, CanWork: true, CanReview: true), Token));

		Assert.False(created.CanMerge, "An agent is only the merge agent when it is asked to be.");
	}

	[Fact]
	public async Task CreateAsync_OfASecondMergeAgent_IsRefusedWithTheNameOfTheFirstAndCreatesNothing()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(Input("Second", canMerge: true), Token));

		Assert.Equal(TakenMessage, message);
		Agent only = Assert.Single(await service.ListAsync(Token));
		Assert.Equal("Merger", only.Name);
	}

	[Fact]
	public async Task CreateAsync_OfAnAgentWithoutTheMergeFlag_IsAllowedWhileAnotherAgentIsTheMergeAgent()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.CreateAsync(Input("Worker", canMerge: false), Token));
		ResultAssert.Succeeded(await service.CreateAsync(Input("Reviewer", canMerge: false, canWork: false), Token));

		Assert.Equal(["Merger"], (await service.ListAsync(Token)).Where(agent => agent.CanMerge).Select(agent => agent.Name));
		Assert.Equal(3, (await service.ListAsync(Token)).Count);
	}

	[Fact]
	public async Task CreateAsync_OfASecondMergeAgentWhoseNameIsAlsoTaken_ReportsTheNameNotTheMergeRole()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(Input("merger", canMerge: true), Token));

		Assert.Equal("An agent named 'merger' already exists.", message);
	}

	[Fact]
	public async Task UpdateAsync_GivingTheMergeRoleToASecondAgent_IsRefusedWithTheNameOfTheFirstAndChangesNothing()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		Agent other = await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(other.Id, Input("Renamed", canMerge: true), Token));

		Assert.Equal(TakenMessage, message);
		Agent? stored = await TestData.FindAgentAsync(database, other.Id);
		Assert.NotNull(stored);
		Assert.Equal("Other", stored.Name);
		Assert.False(stored.CanMerge, "The refused change must not be applied.");
		Assert.Equal(["Merger"], (await service.ListAsync(Token)).Where(agent => agent.CanMerge).Select(agent => agent.Name));
	}

	[Fact]
	public async Task UpdateAsync_KeepingTheMergeRole_IsAllowedAndSavesTheOtherChanges()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent updated = ResultAssert.Succeeded(await service.UpdateAsync(merger.Id, new AgentInput("Resolver", HarnessKind.Codex, "codex", "--full-auto", CanWork: false, CanReview: false, CanMerge: true), Token));

		Assert.True(updated.CanMerge, "The agent keeps the role it already had.");
		Agent? stored = await TestData.FindAgentAsync(database, merger.Id);
		Assert.NotNull(stored);
		Assert.Equal("Resolver", stored.Name);
		Assert.Equal(HarnessKind.Codex, stored.Harness);
		Assert.True(stored.CanMerge, "The role is kept.");
	}

	[Fact]
	public async Task UpdateAsync_ClearingTheMergeRole_IsAllowedAndFreesItForAnotherAgent()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		Agent other = await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(merger.Id, Input("Merger", canMerge: false, canWork: false, canReview: false), Token));
		Agent taken = ResultAssert.Succeeded(await service.UpdateAsync(other.Id, Input("Other", canMerge: true), Token));

		Assert.True(taken.CanMerge, "Another agent can take the role once it is free.");
		Assert.False((await TestData.FindAgentAsync(database, merger.Id))?.CanMerge, "The first agent gave the role up.");
		Assert.Equal(["Other"], (await service.ListAsync(Token)).Where(agent => agent.CanMerge).Select(agent => agent.Name));
	}

	[Fact]
	public async Task UpdateAsync_ClearingTheMergeRoleOfAnAgentWithNothingElse_IsAllowedWhileCardsAreInTheMiddleOfTheirRun()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		await TestData.AddCardAsync(database, "Busy", CardLane.InProgress);
		await TestData.AddCardAsync(database, "Waiting", CardLane.Review);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(merger.Id, Input("Merger", canMerge: false, canWork: false, canReview: false), Token));

		Assert.False((await TestData.FindAgentAsync(database, merger.Id))?.CanMerge, "No card holds the merge role, so nothing keeps it.");
	}

	[Fact]
	public async Task DeleteAsync_OfTheMergeAgent_IsAllowedAndFreesTheRoleForAnotherAgent()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		await TestData.AddCardAsync(database, "Busy", CardLane.InProgress);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(merger.Id, Token));
		ResultAssert.Succeeded(await service.CreateAsync(Input("Next", canMerge: true), Token));

		Assert.Null(await TestData.FindAgentAsync(database, merger.Id));
		Assert.Equal(["Next"], (await service.ListAsync(Token)).Where(agent => agent.CanMerge).Select(agent => agent.Name));
	}

	[Fact]
	public async Task DeleteAsync_OfTheMergeAgentThatIsAlsoTheWorkerOfAnUnfinishedCard_StillRefusesBecauseOfTheWorkerRoleNotTheMergeRole()
	{
		await using TestDatabase database = new();
		Agent both = await TestData.AddAgentAsync(database, "Both", canWork: true, canReview: false, canMerge: true);
		await TestData.AddCardAsync(database, "Busy", CardLane.InProgress, workerAgentId: both.Id);
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(both.Id, Token));

		Assert.Equal("Agent 'Both' is assigned to 1 card that is not complete (1 as worker) and cannot be deleted.", message);
		Assert.NotNull(await TestData.FindAgentAsync(database, both.Id));
	}

	[Fact]
	public async Task CardAssignment_OfAnAgentThatOnlyMergesAsWorkerOrReviewer_IsRefused()
	{
		await using PipelineRig rig = new();
		Agent merger = await TestData.AddAgentAsync(rig.Database, "Merger", canWork: false, canReview: false, canMerge: true);

		string asWorker = ResultAssert.Failed(await rig.Cards.CreateAsync(await TestCardInput.CreateAsync(rig.Database, "Card", workerAgentId: merger.Id), Token));
		string asReviewer = ResultAssert.Failed(await rig.Cards.CreateAsync(await TestCardInput.CreateAsync(rig.Database, "Card", reviewerAgentId: merger.Id), Token));

		Assert.Equal("Agent 'Merger' cannot be assigned as a worker.", asWorker);
		Assert.Equal("Agent 'Merger' cannot be assigned as a reviewer.", asReviewer);
	}
}
