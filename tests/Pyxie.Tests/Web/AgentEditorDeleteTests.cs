using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>An agent is deleted from its editor: Delete asks for confirmation in the dialog, a refusal shows in the dialog's alert, and a deletion closes the dialog and removes the row.</summary>
public sealed class AgentEditorDeleteTests
{
	[Fact]
	public async Task Delete_IsOfferedForAnExistingAgentAndNotForANewOne()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		await host.ClickAsync(await host.FindButtonAsync("Add agent"));
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Name == "button" && element.Text == "Delete");
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.FindButtonAsync("Delete");
	}

	[Fact]
	public async Task Delete_AsksForConfirmationInPlaceAndRemovesNothingYet()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));

		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.FindButtonAsync("Confirm deleting Claude");
		await host.FindButtonAsync("Cancel deleting Claude");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Name == "button" && element.Text is "Save" or "Delete");
		Assert.Single(await ReadAgentsAsync(database));
		Assert.True(await host.IsModalOpenAsync(), "Asking for confirmation should keep the editor open.");
	}

	[Fact]
	public async Task Delete_PutsCancelWhereDeleteWasSoASecondClickBacksOut()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		List<string> before = await FooterButtonsAsync(host);
		Assert.Equal(["Delete", "Cancel", "Save"], before);

		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		Assert.Equal(["Cancel deleting Claude", "Confirm deleting Claude"], await FooterButtonsAsync(host));
	}

	[Fact]
	public async Task CancellingTheConfirmation_KeepsTheAgentAndOffersDeleteCancelAndSaveAgain()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Cancel deleting Claude"));

		Assert.Equal(["Delete", "Cancel", "Save"], await FooterButtonsAsync(host));
		Assert.Single(await ReadAgentsAsync(database));
		Assert.True(await host.IsModalOpenAsync(), "Backing out of the confirmation should keep the editor open.");
	}

	[Fact]
	public async Task ConfirmingDeletesTheAgentClosesTheEditorAndRemovesItsRow()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));

		Assert.Equal(["Codex"], (await ReadAgentsAsync(database)).Select(agent => agent.Name));
		Assert.False(await host.IsModalOpenAsync(), "A deleted agent should close the editor.");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") == "Edit Claude");
		await host.FindButtonAsync("Edit Codex");
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task DeletingAnAgentACardStillNeeds_ShowsTheMessageInTheEditorKeepsTheAgentAndTheEditorOpen()
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Fix it", workerAgentId: claude.Id);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));

		string alert = Assert.Single(await host.AlertsAsync());
		Assert.StartsWith("Agent 'Claude' is assigned to 1 card that is not complete", alert, StringComparison.Ordinal);
		Assert.EndsWith("cannot be deleted.", alert, StringComparison.Ordinal);
		Assert.Single(await ReadAgentsAsync(database));
		Assert.True(await host.IsModalOpenAsync(), "A refused delete should keep the editor open.");
		Assert.Equal(["Delete", "Cancel", "Save"], await FooterButtonsAsync(host));
		await host.FindButtonAsync("Edit Claude");
	}

	[Fact]
	public async Task OpeningTheEditorAgainAfterARefusedDelete_ShowsNoStaleMessageAndNoPendingConfirmation()
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Fix it", workerAgentId: claude.Id);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));
		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));

		Assert.Empty(await host.AlertsAsync());
		Assert.Equal(["Delete", "Cancel", "Save"], await FooterButtonsAsync(host));
	}

	// The buttons of the dialog's footer, by their accessible names, in order.
	private static async Task<List<string>> FooterButtonsAsync(GateInteractiveHost host)
	{
		IReadOnlyList<GateRenderedElement> elements = await host.ElementsAsync();
		int footer = elements.ToList().FindIndex(element => (element.Attribute("class") ?? string.Empty).Split(' ').Contains("editor-footer"));
		Assert.True(footer >= 0, "The editor should have a footer.");

		return [.. elements.Skip(footer + 1).Where(element => element.Name == "button").Select(element => element.Attribute("aria-label") ?? element.Text)];
	}

	private static async Task<List<Agent>> ReadAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().OrderBy(agent => agent.Id).ToListAsync(TestContext.Current.CancellationToken);
	}
}
