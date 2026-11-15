using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class AgentEditorMergeTests
{
	private const string MergeLabel = "Resolves merge conflicts";

	[Fact]
	public async Task TheEditorOffersTheMergeRoleAfterTheOtherTwoAndStartsItCleared()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);

		await host.StartAsync<AgentEditor>(NewAgent());

		Assert.Equal(["Can work", "Can review", MergeLabel], await host.TextsWithClassAsync("ant-checkbox-label"));
		Assert.Null((await host.FindCheckboxAsync(MergeLabel)).Attribute("checked"));
		Assert.Equal("true", (await host.FindCheckboxAsync("Can work")).Attribute("checked"));
		Assert.Equal("true", (await host.FindCheckboxAsync("Can review")).Attribute("checked"));
	}

	[Fact]
	public async Task SavingANewAgentWithTheMergeRoleTickedStoresItAsTheMergeAgent()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Merger");

		await host.SetCheckedAsync(await host.FindCheckboxAsync(MergeLabel), true);
		Assert.Equal("true", (await host.FindCheckboxAsync(MergeLabel)).Attribute("checked"));
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Agent saved = await ReadTheOnlyAgentAsync(database);
		Assert.True(saved.CanMerge, "The agent should have been saved as the merge agent.");
		Assert.True(saved.CanWork, "Ticking the merge role should not change the other roles.");
		Assert.True(saved.CanReview, "Ticking the merge role should not change the other roles.");
	}

	[Fact]
	public async Task SavingANewAgentWithoutTouchingTheMergeRoleStoresNoMergeAgent()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Worker");

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.False((await ReadTheOnlyAgentAsync(database)).CanMerge, "An agent saved without choosing the merge role should not be the merge agent.");
	}

	[Fact]
	public async Task EditingTheMergeAgentShowsTheRoleTickedAndSavingKeepsIt()
	{
		await using TestDatabase database = new();
		Agent stored = await AgentTestData.AddAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(Editing(stored));
		Assert.Equal("true", (await host.FindCheckboxAsync(MergeLabel)).Attribute("checked"));

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.True((await ReadTheOnlyAgentAsync(database)).CanMerge, "Saving the merge agent without touching the role should keep it.");
	}

	[Fact]
	public async Task ClearingTheMergeRoleOfTheMergeAgentSavesItWithoutTheRole()
	{
		await using TestDatabase database = new();
		Agent stored = await AgentTestData.AddAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(Editing(stored));

		await host.SetCheckedAsync(await host.FindCheckboxAsync(MergeLabel), false);
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.False((await ReadTheOnlyAgentAsync(database)).CanMerge, "Clearing the merge role should save the agent without it.");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AServiceRefusalOfASecondMergeAgentShowsItsMessageInTheAlertAndKeepsTheEditorOpen(bool editing)
	{
		const string Refusal = "Agent 'Merger' already resolves merge conflicts. Only one agent can.";
		await using TestDatabase database = new();
		Agent stored = new() { Id = 7, Name = "Second", Command = "claude", CanWork = true, CanReview = true };
		RefusingAgentService service = new(Refusal);
		await using ServiceProvider services = GateRenderHost.CreateServices(database, service);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(editing ? Editing(stored) : NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Second");

		await host.SetCheckedAsync(await host.FindCheckboxAsync(MergeLabel), true);
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal([Refusal], await host.AlertsAsync());
		Assert.True(await host.IsModalOpenAsync(), "A refused save should keep the editor open.");
		Assert.True(Assert.Single(service.Requests).CanMerge, "The editor should have asked for the merge role.");
		Assert.Empty(await host.TextsWithClassAsync("field-error"));
	}

	private static Dictionary<string, object?> NewAgent()
	{
		return new Dictionary<string, object?> { ["Visible"] = true };
	}

	private static Dictionary<string, object?> Editing(Agent agent)
	{
		return new Dictionary<string, object?> { ["Visible"] = true, ["Agent"] = agent };
	}

	private static async Task<Agent> ReadTheOnlyAgentAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
	}
}
