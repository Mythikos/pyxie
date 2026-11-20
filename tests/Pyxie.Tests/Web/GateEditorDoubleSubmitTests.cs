using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The gate editor acts once per opening, on the gate it was opened for. Clicks the person made before the browser showed the result of an earlier one still reach the server: a second Save after the editor closed must not save again, and an Edit click on another row that arrives after the editor opened must not turn Save or Delete into a write of that row.</summary>
public sealed class GateEditorDoubleSubmitTests
{
	[Fact]
	public async Task Save_AfterAnEditClickOnAnotherRowArrivedLate_SavesTheGateTheFormShows()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Test");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await OpenBuildWithALateEditOfTestAsync(host);

		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Compile");
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		List<Gate> stored = await GateTestData.ReadAsync(database);
		Assert.Equal("Compile", Assert.Single(stored, gate => gate.Id == gates["Build"].Id).Name);
		Assert.Equal("Test", Assert.Single(stored, gate => gate.Id == gates["Test"].Id).Name);
		Assert.Empty(await host.AlertsAsync());
		Assert.False(await host.IsModalOpenAsync(), "The saved gate should have closed the editor.");
	}

	[Fact]
	public async Task Delete_AfterAnEditClickOnAnotherRowArrivedLate_DeletesTheGateTheFormShows()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Test");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await OpenBuildWithALateEditOfTestAsync(host);

		await host.ClickAsync(await host.FindButtonAsync("Delete"));
		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Build"));

		Assert.Equal([gates["Test"].Id], (await GateTestData.ReadAsync(database)).Select(gate => gate.Id));
		Assert.Empty(await host.AlertsAsync());
		Assert.False(await host.IsModalOpenAsync(), "The deleted gate should have closed the editor.");
	}

	[Fact]
	public async Task Save_PressedAgainAfterTheEditorClosed_SavesNothingMore()
	{
		await using TestDatabase database = new();
		GatedGateService gates = new(new GateService(database.ContextFactory));
		await using ServiceProvider services = GateRenderHost.CreateServices(database, gateService: gates);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Format");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");
		GateRenderedElement save = await host.FindButtonAsync("Save");
		await host.HoldAcknowledgementsAsync();

		await host.ClickAsync(save);
		await host.ClickAsync(save);
		await host.AcknowledgeAsync();

		Assert.Equal(1, gates.Creates.Started);
		Assert.Equal("Format", Assert.Single(await GateTestData.ReadAsync(database)).Name);
		Assert.Empty(await host.AlertsAsync());
		Assert.False(await host.IsModalOpenAsync(), "The saved gate should have closed the editor.");
	}

	// Opens the editor on Build, then lets an Edit click on Test arrive that the person made before the browser showed the editor.
	private static async Task OpenBuildWithALateEditOfTestAsync(GateInteractiveHost host)
	{
		GateRenderedElement editTest = await host.FindButtonAsync("Edit Test");
		await host.HoldAcknowledgementsAsync();
		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));
		await host.ClickAsync(editTest);
		await host.AcknowledgeAsync();
	}
}
