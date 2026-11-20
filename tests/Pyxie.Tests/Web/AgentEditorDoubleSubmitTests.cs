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

/// <summary>The agent editor acts once per opening, on the agent it was opened for. Clicks the person made before the browser showed the result of an earlier one still reach the server: a second Save after the editor closed must not save again, and an Edit click on another row that arrives after the editor opened must not turn Save into a write of that row.</summary>
public sealed class AgentEditorDoubleSubmitTests
{
	[Fact]
	public async Task Save_AfterAnEditClickOnAnotherRowArrivedLate_SavesTheAgentTheFormShows()
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		Agent codex = await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true, harness: HarnessKind.Codex, command: "codex");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		GateRenderedElement editCodex = await host.FindButtonAsync("Edit Codex");
		await host.HoldAcknowledgementsAsync();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(editCodex);
		await host.AcknowledgeAsync();

		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Claude Code");
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		List<Agent> stored = await ReadAgentsAsync(database);
		Assert.Equal(("Claude Code", "claude"), NameAndCommand(stored, claude.Id));
		Assert.Equal(("Codex", "codex"), NameAndCommand(stored, codex.Id));
		Assert.Empty(await host.AlertsAsync());
		Assert.False(await host.IsModalOpenAsync(), "The saved agent should have closed the editor.");
	}

	[Fact]
	public async Task Delete_AfterAnEditClickOnAnotherRowArrivedLate_DeletesTheAgentTheFormShows()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		Agent codex = await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true, harness: HarnessKind.Codex, command: "codex");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		GateRenderedElement editCodex = await host.FindButtonAsync("Edit Codex");
		await host.HoldAcknowledgementsAsync();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(editCodex);
		await host.AcknowledgeAsync();

		await host.ClickAsync(await host.FindButtonAsync("Delete"));
		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));

		Assert.Equal([codex.Id], (await ReadAgentsAsync(database)).Select(agent => agent.Id));
		Assert.Empty(await host.AlertsAsync());
		Assert.False(await host.IsModalOpenAsync(), "The deleted agent should have closed the editor.");
	}

	[Fact]
	public async Task Save_PressedAgainAfterTheEditorClosed_SavesNothingMore()
	{
		await using TestDatabase database = new();
		HoldingAgentService agents = new(new AgentService(database.ContextFactory, database.Clock));
		await using ServiceProvider services = GateRenderHost.CreateServices(database, agents);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add agent"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Claude");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "claude");
		GateRenderedElement save = await host.FindButtonAsync("Save");
		await host.HoldAcknowledgementsAsync();

		await host.ClickAsync(save);
		await host.ClickAsync(save);
		await host.AcknowledgeAsync();

		Assert.Equal(1, agents.Creates.Started);
		Assert.Equal("Claude", Assert.Single(await ReadAgentsAsync(database)).Name);
		Assert.Empty(await host.AlertsAsync());
		Assert.False(await host.IsModalOpenAsync(), "The saved agent should have closed the editor.");
	}

	private static (string Name, string Command) NameAndCommand(List<Agent> agents, int id)
	{
		Agent agent = Assert.Single(agents, agent => agent.Id == id);

		return (agent.Name, agent.Command);
	}

	private static async Task<List<Agent>> ReadAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().OrderBy(agent => agent.Id).ToListAsync(TestContext.Current.CancellationToken);
	}
}
