using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A gate is deleted from its editor: Delete asks for confirmation in the dialog, backing out keeps the gate, and the gates after a deleted one close up the order.</summary>
public sealed class GateEditorDeleteTests
{
	[Fact]
	public async Task Delete_IsOfferedForAnExistingGateAndNotForANewOne()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.ClickAsync(await host.FindButtonAsync("Add gate"));
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Name == "button" && element.Text == "Delete");
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));
		await host.FindButtonAsync("Delete");
	}

	[Fact]
	public async Task Delete_AsksForConfirmationAndRemovesNothingYet()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));

		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		Assert.Equal("Build,Tests", await GateTestData.ReadNamesAsync(database));
		await host.FindButtonAsync("Confirm deleting Build");
		await host.FindButtonAsync("Cancel deleting Build");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Name == "button" && element.Text is "Save" or "Delete");
		Assert.True(await host.IsModalOpenAsync(), "Asking for confirmation should keep the editor open.");
	}

	[Fact]
	public async Task CancellingTheConfirmationKeepsTheGateAndOffersDeleteCancelAndSaveAgain()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Cancel deleting Build"));

		Assert.Equal("Build,Tests", await GateTestData.ReadNamesAsync(database));
		await host.FindButtonAsync("Delete");
		await host.FindButtonAsync("Cancel");
		await host.FindButtonAsync("Save");
		Assert.True(await host.IsModalOpenAsync(), "Backing out of the confirmation should keep the editor open.");
	}

	[Fact]
	public async Task ConfirmingRemovesTheGateClosesTheEditorAndTheGatesAfterItCloseUpTheOrder()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Tests"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Tests"));

		Assert.Equal("Build,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1", await GateTestData.ReadPositionsAsync(database));
		Assert.Equal(["Build", "Lint"], await host.TextsWithClassAsync("gate-text"));
		Assert.False(await host.IsModalOpenAsync(), "A deleted gate should close the editor.");
		await host.FindButtonAsync("Edit Build");
		await host.FindButtonAsync("Edit Lint");
	}

	[Fact]
	public async Task EditingAnotherGateAfterBackingOutOfAConfirmationStartsWithoutOne()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));
		await host.ClickAsync(await host.FindButtonAsync("Cancel deleting Build"));
		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		await host.ClickAsync(await host.FindButtonAsync("Edit Tests"));

		await host.FindButtonAsync("Delete");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label")?.StartsWith("Confirm deleting", StringComparison.Ordinal) == true);
	}

	[Fact]
	public async Task DeletingAGateThatWasDeletedMeanwhileShowsTheMessageInTheEditorAndTheListIsReadAgainOnceTheEditorCloses()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Tests"));
		await host.ClickAsync(await host.FindButtonAsync("Delete"));
		await new GateService(database.ContextFactory).DeleteAsync(gates["Tests"].Id, TestContext.Current.CancellationToken);

		await host.ClickAsync(await host.FindButtonAsync("Confirm deleting Tests"));

		Assert.Equal(["Gate not found."], await host.AlertsAsync());
		Assert.True(await host.IsModalOpenAsync(), "A refused delete should keep the editor open.");
		await host.FindButtonAsync("Delete");

		await host.ClickAsync(await host.FindButtonAsync("Cancel"));

		Assert.Equal(["Build"], await host.TextsWithClassAsync("gate-text"));
	}
}
