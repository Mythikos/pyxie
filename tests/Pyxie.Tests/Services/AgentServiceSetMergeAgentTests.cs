using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Making an agent the merge agent moves the role in one transaction: the project has exactly that one merge agent afterwards, however the agents are ordered, and a refused change leaves the current merge agent in place.</summary>
public sealed class AgentServiceSetMergeAgentTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<string[]> MergeAgentsAsync(AgentService service)
	{
		return [.. (await service.ListAsync(Token)).Where(agent => agent.CanMerge).Select(agent => agent.Name)];
	}

	// Every field the role change could touch, one line per agent, so a failed comparison shows which agent changed.
	private static async Task<string[]> RowsAsync(AgentService service)
	{
		return [.. (await service.ListAsync(Token)).OrderBy(agent => agent.Id).Select(agent => $"{agent.Id} {agent.Name} work={agent.CanWork} review={agent.CanReview} merge={agent.CanMerge}")];
	}

	[Fact]
	public async Task SetMergeAgentAsync_WhenNoAgentHasTheRole_GivesItToTheAgent()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "First", canWork: true, canReview: true);
		Agent second = await TestData.AddAgentAsync(database, "Second", canWork: true, canReview: false);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.SetMergeAgentAsync(second.Id, Token));

		Assert.Equal(["Second"], await MergeAgentsAsync(service));
	}

	// The unique index on the merge role checks each row as it is written, so the order of the two agents' rows matters to a careless implementation: both directions are tried.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task SetMergeAgentAsync_MovesTheRoleFromOneAgentToAnotherWhicheverWasAddedFirst(bool holderAddedFirst)
	{
		await using TestDatabase database = new();
		Agent first = await TestData.AddAgentAsync(database, "First", canWork: true, canReview: true, canMerge: holderAddedFirst);
		Agent second = await TestData.AddAgentAsync(database, "Second", canWork: true, canReview: true, canMerge: !holderAddedFirst);
		await TestData.AddAgentAsync(database, "Bystander", canWork: false, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);
		Agent target = holderAddedFirst ? second : first;

		ResultAssert.Succeeded(await service.SetMergeAgentAsync(target.Id, Token));

		Assert.Equal([target.Name], await MergeAgentsAsync(service));
	}

	[Fact]
	public async Task SetMergeAgentAsync_LeavesEveryOtherFieldOfEveryAgentAlone()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: true, canMerge: true);
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.SetMergeAgentAsync(worker.Id, Token));

		Assert.Equal([$"{merger.Id} Merger work=False review=True merge=False", $"{worker.Id} Worker work=True review=False merge=True"], await RowsAsync(service));
	}

	[Fact]
	public async Task SetMergeAgentAsync_WithNoAgent_ClearsTheRoleFromEveryAgent()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.SetMergeAgentAsync(null, Token));

		Assert.Empty(await MergeAgentsAsync(service));
		Assert.Equal(2, (await service.ListAsync(Token)).Count);
	}

	[Fact]
	public async Task SetMergeAgentAsync_WithNoAgentWhenNoneHasTheRole_SucceedsAndChangesNothing()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		AgentService service = new(database.ContextFactory, database.Clock);
		string[] before = await RowsAsync(service);

		ResultAssert.Succeeded(await service.SetMergeAgentAsync(null, Token));

		Assert.Equal(before, await RowsAsync(service));
	}

	[Fact]
	public async Task SetMergeAgentAsync_ForTheAgentThatHasTheRole_SucceedsAndChangesNothing()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		AgentService service = new(database.ContextFactory, database.Clock);
		string[] before = await RowsAsync(service);

		ResultAssert.Succeeded(await service.SetMergeAgentAsync(merger.Id, Token));

		Assert.Equal(before, await RowsAsync(service));
		Assert.Equal(["Merger"], await MergeAgentsAsync(service));
	}

	[Fact]
	public async Task SetMergeAgentAsync_ForAMissingAgent_IsRefusedAndTheCurrentMergeAgentKeepsTheRole()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		AgentService service = new(database.ContextFactory, database.Clock);
		string[] before = await RowsAsync(service);

		string error = ResultAssert.Failed(await service.SetMergeAgentAsync(999, Token));

		Assert.Equal("Agent not found.", error);
		Assert.Equal(before, await RowsAsync(service));
		Assert.Equal(["Merger"], await MergeAgentsAsync(service));
	}

	[Fact]
	public async Task SetMergeAgentAsync_ThatMovedTheRole_LetsTheNextCreateOfAMergeAgentBeRefusedByTheNewHolder()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Old", canWork: true, canReview: true, canMerge: true);
		Agent chosen = await TestData.AddAgentAsync(database, "Chosen", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);
		ResultAssert.Succeeded(await service.SetMergeAgentAsync(chosen.Id, Token));

		string message = ResultAssert.Failed(await service.CreateAsync(new AgentInput("Third", HarnessKind.ClaudeCode, "claude", string.Empty, CanWork: true, CanReview: true, CanMerge: true), Token));

		Assert.Equal("Agent 'Chosen' already resolves merge conflicts. Only one agent can.", message);
	}
}
