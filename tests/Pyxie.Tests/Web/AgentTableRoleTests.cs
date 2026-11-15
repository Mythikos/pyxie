using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The roles in the agents table save at once: Work and Review are checkboxes, and Resolve is one choice among the agents and None. A refusal shows its message and the controls show what is stored.</summary>
public sealed class AgentTableRoleTests
{
	[Fact]
	public async Task ClearingWork_SavesAtOnceAndKeepsEveryOtherFieldOfTheAgent()
	{
		await using TestDatabase database = new();
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: true, canReview: true, HarnessKind.Codex, "codex", "exec --name {CARD_NAME}", canMerge: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		await host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Merger can work"), false);

		Agent stored = Assert.Single(await ReadAgentsAsync(database));
		Assert.Equal((merger.Id, "Merger", HarnessKind.Codex, "codex", "exec --name {CARD_NAME}", false, true, true), (stored.Id, stored.Name, stored.Harness, stored.Command, stored.Arguments, stored.CanWork, stored.CanReview, stored.CanMerge));
		Assert.Empty(await host.AlertsAsync());
		Assert.Null((await host.FindNamedCheckboxAsync("Merger can work")).Attribute("checked"));
		Assert.Equal("true", (await host.FindNamedCheckboxAsync("Merger can review")).Attribute("checked"));
	}

	[Fact]
	public async Task SettingReview_SavesAtOnce()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		await host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Claude can review"), true);

		Agent stored = Assert.Single(await ReadAgentsAsync(database));
		Assert.True(stored.CanReview, "The agent should now be able to review.");
		Assert.True(stored.CanWork, "Setting one role should not change the other.");
		Assert.Equal("true", (await host.FindNamedCheckboxAsync("Claude can review")).Attribute("checked"));
	}

	[Fact]
	public async Task ASavedRole_KeepsTheRowsAsTheyWere()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		List<object?> before = await RowKeysAsync(host);

		await host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Claude can review"), false);

		Assert.Equal(before, await RowKeysAsync(host));
	}

	[Theory]
	[InlineData("work", "worker", true)]
	[InlineData("review", "reviewer", false)]
	public async Task ClearingARoleACardStillNeeds_ShowsTheMessageAndTheCheckboxShowsTheRoleAgain(string role, string cardRole, bool asWorker)
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		Agent other = await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Fix it", workerAgentId: asWorker ? claude.Id : other.Id, reviewerAgentId: asWorker ? null : claude.Id);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		List<object?> before = await RowKeysAsync(host);
		GateRenderedElement checkbox = await host.FindNamedCheckboxAsync($"Claude can {role}");
		Assert.Equal("true", checkbox.Attribute("checked"));

		await host.SetCheckedAsync(checkbox, false);

		Assert.Equal([$"Agent 'Claude' is the {cardRole} of 1 card that is not complete and must keep the {cardRole} role."], await host.AlertsAsync());
		Assert.Equal("true", (await host.FindNamedCheckboxAsync($"Claude can {role}")).Attribute("checked"));
		Agent stored = (await ReadAgentsAsync(database)).Single(agent => agent.Id == claude.Id);
		Assert.True(stored.CanWork && stored.CanReview, "A refused change should store nothing.");
		Assert.NotEqual(before, await RowKeysAsync(host));
	}

	[Fact]
	public async Task ThePageAcceptsTheNextRoleChangeAfterARefusalAndClearsItsMessage()
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Fix it", workerAgentId: claude.Id);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Claude can work"), false);
		Assert.Single(await host.AlertsAsync());

		await host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Claude can review"), false);

		Assert.Empty(await host.AlertsAsync());
		Assert.False(Assert.Single(await ReadAgentsAsync(database)).CanReview, "The review role should have been cleared.");
	}

	[Fact]
	public async Task ChoosingAnAgentToResolveConflicts_MakesItTheOnlyOneAndTakesTheRoleFromTheOther()
	{
		await using TestDatabase database = new();
		await AgentTestData.AddAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		Agent claude = await AgentTestData.AddAsync(database, "Claude", canWork: true, canReview: true, canMerge: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		await host.SetCheckedAsync(await host.FindRadioAsync("Claude resolves merge conflicts"), true);

		Assert.Equal([claude.Id], (await ReadAgentsAsync(database)).Where(agent => agent.CanMerge).Select(agent => agent.Id));
		Assert.Equal("true", (await host.FindRadioAsync("Claude resolves merge conflicts")).Attribute("checked"));
		Assert.Null((await host.FindRadioAsync("Merger resolves merge conflicts")).Attribute("checked"));
		Assert.Null((await host.FindRadioAsync("No agent resolves merge conflicts")).Attribute("checked"));
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task ChoosingNone_TakesTheRoleFromTheAgentThatHasIt()
	{
		await using TestDatabase database = new();
		await AgentTestData.AddAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		await host.SetCheckedAsync(await host.FindRadioAsync("No agent resolves merge conflicts"), true);

		Assert.DoesNotContain(await ReadAgentsAsync(database), agent => agent.CanMerge);
		Assert.Equal("true", (await host.FindRadioAsync("No agent resolves merge conflicts")).Attribute("checked"));
		Assert.Null((await host.FindRadioAsync("Merger resolves merge conflicts")).Attribute("checked"));
	}

	[Fact]
	public async Task ChoosingAnAgentThatWasDeletedMeanwhile_ShowsTheMessageAndTheRadiosShowTheStoredChoice()
	{
		await using TestDatabase database = new();
		await AgentTestData.AddAsync(database, "Merger", canWork: true, canReview: true, canMerge: true);
		Agent gone = await AgentTestData.AddAsync(database, "Gone", canWork: true, canReview: true, canMerge: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		List<object?> before = await RowKeysAsync(host);
		await using (ProjectDbContext context = database.CreateContext())
		{
			await context.Agents.Where(agent => agent.Id == gone.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
		}

		await host.SetCheckedAsync(await host.FindRadioAsync("Gone resolves merge conflicts"), true);

		Assert.Equal(["Agent not found."], await host.AlertsAsync());
		Assert.Equal("true", (await host.FindRadioAsync("Merger resolves merge conflicts")).Attribute("checked"));
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") == "Gone resolves merge conflicts");
		Assert.NotEqual(before, await RowKeysAsync(host));
		Assert.True(Assert.Single(await ReadAgentsAsync(database)).CanMerge, "A refused choice should leave the merge agent as it was.");
	}

	[Fact]
	public async Task WhileARoleIsBeingSaved_EveryControlOfTheTableIsDisabled()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true);
		HoldingAgentService service = new(new AgentService(database.ContextFactory, database.Clock));
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		service.Hold = release.Task;
		await using ServiceProvider services = GateRenderHost.CreateServices(database, service);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		Task write = host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Claude can work"), false);
		try
		{
			await service.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

			IReadOnlyList<GateRenderedElement> inputs = await RoleInputsAsync(host);
			Assert.Equal(7, inputs.Count);
			Assert.All(inputs, input => Assert.True(input.Attributes.ContainsKey("disabled"), $"The input '{input.Attribute("aria-label")}' should be disabled while a role is saved."));
			Assert.False((await host.FindButtonAsync("Edit Claude")).IsClickable, "An agent cannot be opened while a role is saved.");
			Assert.False((await host.FindButtonAsync("Add agent")).IsClickable, "An agent cannot be added while a role is saved.");
		}
		finally
		{
			release.TrySetResult();
			await write;
		}

		IReadOnlyList<GateRenderedElement> afterwards = await RoleInputsAsync(host);
		Assert.All(afterwards, input => Assert.False(input.Attributes.ContainsKey("disabled"), $"The input '{input.Attribute("aria-label")}' should work again once the role is saved."));
		Assert.False((await ReadAgentsAsync(database)).Single(agent => agent.Name == "Claude").CanWork, "The held write should have gone through once released.");
	}

	// The checkboxes and radio buttons of the table, which carry the name of their agent; the editor's own checkboxes are named by their labels.
	private static async Task<IReadOnlyList<GateRenderedElement>> RoleInputsAsync(GateInteractiveHost host)
	{
		return await host.FindElementsAsync("input", input => input.Attribute("type") is "checkbox" or "radio" && input.Attribute("aria-label") is not null);
	}

	private static async Task<List<Agent>> ReadAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().OrderBy(agent => agent.Id).ToListAsync(TestContext.Current.CancellationToken);
	}

	// The keys of the rows of the table body. A row with a new key is a new row in the browser.
	private static async Task<List<object?>> RowKeysAsync(GateInteractiveHost host)
	{
		return [.. (await host.FindElementsAsync("tr", row => row.Key is not null)).Select(row => row.Key)];
	}
}
