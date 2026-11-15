using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Delete and the confirmation that replaces it are different buttons, so the footer moves the keyboard focus along: Delete hands it to Cancel, which stands where Delete was, and Delete gets it back when the confirmation ends and the dialog stays.</summary>
public sealed class EditorFooterFocusTests
{
	[Fact]
	public async Task Delete_MovesTheFocusToCancelThatStandsWhereDeleteWas()
	{
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = CreateServices(browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<EditorFooter>(Parameters(() => Task.CompletedTask));

		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		GateRenderedElement cancel = await host.FindButtonAsync("Cancel deleting Claude");
		Assert.Equal(cancel.ReferenceId, FocusedId(browser));
	}

	[Fact]
	public async Task CancelingTheConfirmation_MovesTheFocusBackToDelete()
	{
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = CreateServices(browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<EditorFooter>(Parameters(() => Task.CompletedTask));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Cancel deleting Claude"));

		GateRenderedElement delete = await host.FindButtonAsync("Delete");
		Assert.Equal(delete.ReferenceId, FocusedId(browser));
		Assert.Equal(2, FocusCalls(browser).Count);
	}

	[Fact]
	public async Task ADeleteThatIsRefused_MovesTheFocusBackToDelete()
	{
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = CreateServices(browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<EditorFooter>(Parameters(() => Task.CompletedTask));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));

		GateRenderedElement delete = await host.FindButtonAsync("Delete");
		Assert.Equal(delete.ReferenceId, FocusedId(browser));
		Assert.Equal(2, FocusCalls(browser).Count);
	}

	[Fact]
	public async Task ARefusedDeleteInTheEditor_MovesTheFocusBackToDeleteAndKeepsTheAgent()
	{
		await using TestDatabase database = new();
		Agent claude = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddCardAsync(database, "Fix it", workerAgentId: claude.Id);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));

		Assert.Single(await host.AlertsAsync());
		GateRenderedElement delete = await host.FindButtonAsync("Delete");
		Assert.Equal(delete.ReferenceId, FocusedId(browser));
		Assert.Equal(2, FocusCalls(browser).Count);
	}

	[Fact]
	public async Task AConfirmedDeleteInTheEditor_DeletesAndLeavesTheFocusAloneBecauseTheDialogCloses()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));
		Assert.Single(FocusCalls(browser));

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Claude"));

		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") == "Edit Claude");
		Assert.Empty(await ReadAgentsAsync(database));
		Assert.Single(FocusCalls(browser));
	}

	private static async Task<List<Agent>> ReadAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
	}

	private static Dictionary<string, object?> Parameters(Func<Task> deleteRequested)
	{
		return new Dictionary<string, object?>
		{
			[nameof(EditorFooter.Subject)] = "Claude",
			[nameof(EditorFooter.Visible)] = true,
			[nameof(EditorFooter.Deletable)] = true,
			[nameof(EditorFooter.DeleteRequested)] = EventCallback.Factory.Create(new object(), deleteRequested),
		};
	}

	private static ServiceProvider CreateServices(IJSRuntime jsRuntime)
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton(jsRuntime);
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();

		return services.BuildServiceProvider();
	}

	private static List<(string Identifier, object?[] Arguments)> FocusCalls(GateStubJsRuntime browser)
	{
		return [.. browser.Calls.Where(call => call.Identifier == GateStubJsRuntime.FocusIdentifier)];
	}

	// The element the browser was last asked to focus.
	private static string? FocusedId(GateStubJsRuntime browser)
	{
		return Assert.IsType<ElementReference>(FocusCalls(browser).Last().Arguments[0]).Id;
	}
}
