using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class GateTableEnabledTests
{
	[Fact]
	public async Task SwitchingAGateOffSavesAtOnceAndChangesNothingElseAboutIt()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		await service.CreateAsync(GateTestInput.Create("Build", "dotnet", "build", 10), TestContext.Current.CancellationToken);
		await service.CreateAsync(GateTestInput.Create("Format", "dotnet", "format --verify-no-changes", 5), TestContext.Current.CancellationToken);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.ClickAsync(await host.FindLabelledSwitchAsync("Enable Format"));

		List<Gate> stored = await GateTestData.ReadAsync(database);
		Assert.Equal(new GateInput("Build", "dotnet", "build", 10, true), ToInput(stored[0]));
		Assert.Equal(new GateInput("Format", "dotnet", "format --verify-no-changes", 5, false), ToInput(stored[1]));
		Assert.Equal("0,1", await GateTestData.ReadPositionsAsync(database));
		Assert.Equal("false", (await host.FindLabelledSwitchAsync("Enable Format")).Attribute("aria-checked"));
		Assert.Equal("true", (await host.FindLabelledSwitchAsync("Enable Build")).Attribute("aria-checked"));
	}

	[Fact]
	public async Task SwitchingADisabledGateOnSavesAtOnce()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Build", 0, isEnabled: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.ClickAsync(await host.FindLabelledSwitchAsync("Enable Build"));

		Assert.True(Assert.Single(await GateTestData.ReadAsync(database)).IsEnabled, "The gate should now be enabled.");
		Assert.Equal("true", (await host.FindLabelledSwitchAsync("Enable Build")).Attribute("aria-checked"));
	}

	[Fact]
	public async Task SwitchingAGateThatWasDeletedMeanwhileShowsTheMessageAndRefreshesTheList()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		ServiceResult deleted = await new GateService(database.ContextFactory).DeleteAsync(gates["Tests"].Id, TestContext.Current.CancellationToken);
		Assert.True(deleted.Succeeded, deleted.Error);

		await host.ClickAsync(await host.FindLabelledSwitchAsync("Enable Tests"));

		Assert.Equal(["Gate not found."], await host.AlertsAsync());
		Assert.Equal(["Build"], await host.TextsWithClassAsync("gate-text"));
	}

	private static GateInput ToInput(Gate gate)
	{
		return new GateInput(gate.Name, gate.Command, gate.Arguments, gate.TimeoutMinutes, gate.IsEnabled);
	}
}
