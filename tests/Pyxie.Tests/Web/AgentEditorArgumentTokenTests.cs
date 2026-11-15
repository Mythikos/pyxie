using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The agent editor and the tokens in an agent's arguments: a name in capitals between braces that is not a token shows its message beside the field and saves nothing, and the tokens of a fixed entry are saved as typed.</summary>
public sealed class AgentEditorArgumentTokenTests
{
	private const string UnknownTokenMessage = "Arguments use {CARDNAME}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";

	[Fact]
	public async Task AnUnknownTokenInTheArgumentsShowsTheMessageBesideTheFieldKeepsTheEditorOpenAndSavesNothing()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Claude");
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "-p --name \"{CARDNAME}\"");

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal([UnknownTokenMessage], await host.TextsWithClassAsync("field-error"));
		Assert.Empty(await host.AlertsAsync());
		Assert.True(await host.IsModalOpenAsync(), "A rejected save should keep the editor open.");
		await using ProjectDbContext context = database.CreateContext();
		Assert.Empty(await context.Agents.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CorrectingTheTokenAndSavingAgainStoresTheArgumentsAsTyped()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Claude");
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "-p --name \"{CARDNAME}\"");
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "-p --name \"{CARD_NAME}\" --branch {BRANCH}");
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Empty(await host.TextsWithClassAsync("field-error"));
		await using ProjectDbContext context = database.CreateContext();
		Agent saved = await context.Agents.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
		Assert.Equal("-p --name \"{CARD_NAME}\" --branch {BRANCH}", saved.Arguments);
	}

	private static Dictionary<string, object?> NewAgent()
	{
		return new Dictionary<string, object?> { ["Visible"] = true };
	}
}
