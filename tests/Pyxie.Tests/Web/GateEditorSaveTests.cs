using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class GateEditorSaveTests
{
	[Fact]
	public async Task AddingAGateSavesItLastWithTheDefaultTimeoutEnabledAndClosesTheEditor()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		Assert.True(await host.IsModalOpenAsync(), "Add gate should open the editor.");

		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "  Format  ");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "format --verify-no-changes");
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		List<Gate> stored = await GateTestData.ReadAsync(database);
		Gate added = stored[^1];
		Assert.Equal(["Build", "Format"], stored.Select(gate => gate.Name));
		Assert.Equal("dotnet", added.Command);
		Assert.Equal("format --verify-no-changes", added.Arguments);
		Assert.Equal(PyxieLimits.DefaultGateTimeoutMinutes, added.TimeoutMinutes);
		Assert.True(added.IsEnabled, "A new gate should be enabled.");
		Assert.False(await host.IsModalOpenAsync(), "A saved gate should close the editor.");
		Assert.Equal(["Build", "Format"], await host.TextsWithClassAsync("gate-text"));
	}

	[Fact]
	public async Task SavingWithTheEnabledSwitchOffStoresADisabledGate()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));

		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Format");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");
		await host.ClickAsync(await host.FindSwitchAsync("_model.IsEnabled"));
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.False(Assert.Single(await GateTestData.ReadAsync(database)).IsEnabled, "The gate should have been saved disabled.");
	}

	[Fact]
	public async Task SavingBlankFieldsShowsEachMessageKeepsTheEditorOpenAndSavesNothing()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		Assert.Empty(await host.TextsWithClassAsync("field-error"));

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal(["Gate name is required.", "Command is required."], await host.TextsWithClassAsync("field-error"));
		Assert.Empty(await host.AlertsAsync());
		Assert.Empty(await GateTestData.ReadAsync(database));
		Assert.True(await host.IsModalOpenAsync(), "A rejected save should keep the editor open.");
	}

	[Theory]
	[InlineData("_model.Name", 101, "Gate name must be 100 characters or fewer.")]
	[InlineData("_model.Command", 501, "Command must be 500 characters or fewer.")]
	[InlineData("_model.Arguments", 2001, "Arguments must be 2000 characters or fewer.")]
	public async Task AFieldOverItsLimitShowsItsMessageAndSavesNothing(string property, int length, string expectedMessage)
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Format");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");
		await host.TypeAsync(await host.FindFieldAsync(property), new string('x', length));

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal([expectedMessage], await host.TextsWithClassAsync("field-error"));
		Assert.Empty(await host.AlertsAsync());
		Assert.Empty(await GateTestData.ReadAsync(database));
	}

	[Fact]
	public async Task AStoredTimeoutOutsideTheAllowedRangeShowsTheTimeoutMessageAndSavesNothing()
	{
		await using TestDatabase database = new();
		await using (ProjectDbContext context = database.CreateContext())
		{
			context.Gates.Add(new Gate { Name = "Slow", Command = "dotnet", TimeoutMinutes = PyxieLimits.MaxGateTimeoutMinutes + 1 });
			await context.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Slow"));

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal(["Timeout must be between 1 and 240 minutes."], await host.TextsWithClassAsync("field-error"));
		Assert.Equal(PyxieLimits.MaxGateTimeoutMinutes + 1, Assert.Single(await GateTestData.ReadAsync(database)).TimeoutMinutes);
	}

	[Fact]
	public async Task EachMessageClearsAsSoonAsItsFieldIsFixed()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Format");

		Assert.Equal(["Command is required."], await host.TextsWithClassAsync("field-error"));
	}

	[Fact]
	public async Task ReopeningTheEditorAfterARejectedSaveStartsBlankWithoutMessages()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "leftover");
		await host.ClickAsync(await host.FindButtonAsync("Save"));
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Add gate"));

		Assert.Empty(await host.TextsWithClassAsync("field-error"));
		Assert.Equal(string.Empty, (await host.FindFieldAsync("_model.Arguments")).Attribute("value") ?? string.Empty);
		Assert.True(await host.IsModalOpenAsync(), "Add gate should open the editor.");
	}

	[Fact]
	public async Task ADuplicateNameShowsTheServicesMessageInTheEditorAndSavesNothing()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "build");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal(["A gate named 'build' already exists."], await host.AlertsAsync());
		Assert.Equal("Build", await GateTestData.ReadNamesAsync(database));
		Assert.True(await host.IsModalOpenAsync(), "A refused save should keep the editor open.");
	}

	[Fact]
	public async Task ReopeningTheEditorAfterARefusedSaveShowsNoStaleServiceMessage()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Build");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");
		await host.ClickAsync(await host.FindButtonAsync("Save"));
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Add gate"));

		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task CancellingClosesTheEditorWithoutSaving()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Format");
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "dotnet");

		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		Assert.Empty(await GateTestData.ReadAsync(database));
		Assert.False(await host.IsModalOpenAsync(), "Cancel should close the editor.");
	}

	[Fact]
	public async Task EditingStartsFromTheGatesValuesAndSavesChangesInPlace()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.ClickAsync(await host.FindButtonAsync("Edit Tests"));

		Assert.True(await host.IsModalOpenAsync(), "Edit should open the editor.");
		Assert.Equal("Tests", (await host.FindFieldAsync("_model.Name")).Attribute("value"));
		Assert.Equal("dotnet", (await host.FindFieldAsync("_model.Command")).Attribute("value"));
		Assert.Equal("test", (await host.FindFieldAsync("_model.Arguments")).Attribute("value"));

		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Unit tests");
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "test --no-build");
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		List<Gate> stored = await GateTestData.ReadAsync(database);
		Assert.Equal(["Build", "Unit tests", "Lint"], stored.Select(gate => gate.Name));
		Assert.Equal("test --no-build", stored[1].Arguments);
		Assert.False(await host.IsModalOpenAsync(), "A saved edit should close the editor.");
		Assert.Equal(["Build", "Unit tests", "Lint"], await host.TextsWithClassAsync("gate-text"));
	}

	[Fact]
	public async Task AddingAfterEditingStartsFromABlankGateNotTheEditedOne()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Add gate"));

		Assert.Equal(string.Empty, (await host.FindFieldAsync("_model.Name")).Attribute("value") ?? string.Empty);
		Assert.Equal(string.Empty, (await host.FindFieldAsync("_model.Command")).Attribute("value") ?? string.Empty);
		Assert.Contains("Add gate", await host.TextsWithClassAsync("ant-modal-title"));
	}
}
