using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A gate moves with the arrow keys on its handle, one place at a time, with the focus staying on the handle, and by dragging the handle to another gate.</summary>
public sealed class GateTableMoveTests
{
	[Fact]
	public async Task ArrowDownOnAHandleRunsTheGateOnePlaceLaterAndShowsTheNewOrder()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync("Move Build"), "ArrowDown");

		Assert.Equal("Tests,Build,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Equal(["Tests", "Build", "Lint"], await host.TextsWithClassAsync("gate-text"));
	}

	[Fact]
	public async Task ArrowUpOnAHandleRunsTheGateOnePlaceEarlierAndShowsTheNewOrder()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync("Move Lint"), "ArrowUp");

		Assert.Equal("Build,Lint,Tests", await GateTestData.ReadNamesAsync(database));
		Assert.Equal(["Build", "Lint", "Tests"], await host.TextsWithClassAsync("gate-text"));
	}

	[Fact]
	public async Task MovingASecondTimeFollowsTheOrderShownAfterTheFirstMove()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync("Move Build"), "ArrowDown");
		await host.PressKeyAsync(await host.FindButtonAsync("Move Build"), "ArrowDown");

		Assert.Equal("Tests,Lint,Build", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task MovesFollowTheListedOrderEvenWhenStoredPositionsHaveGaps()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Build", 5);
		await GateTestData.AddAtAsync(database, "Tests", 9);
		await GateTestData.AddAtAsync(database, "Lint", 20);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync("Move Lint"), "ArrowUp");

		Assert.Equal("Build,Lint,Tests", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task MovingDownFollowsTheListedOrderEvenWhenStoredPositionsHaveGaps()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Build", 5);
		await GateTestData.AddAtAsync(database, "Tests", 9);
		await GateTestData.AddAtAsync(database, "Lint", 20);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync("Move Build"), "ArrowDown");

		Assert.Equal("Tests,Build,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Theory]
	[InlineData("Build", "ArrowUp")]
	[InlineData("Lint", "ArrowDown")]
	[InlineData("Tests", "Tab")]
	[InlineData("Tests", "Enter")]
	[InlineData("Tests", "ArrowLeft")]
	public async Task AKeyThatCannotMoveTheGateChangesNothing(string gate, string key)
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync($"Move {gate}"), key);

		Assert.Equal("Build,Tests,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Empty(await host.AlertsAsync());
		Assert.DoesNotContain(browser.Calls, call => call.Identifier == GateStubJsRuntime.FocusIdentifier);
	}

	[Fact]
	public async Task AfterAMoveByKeyTheFocusGoesBackToTheHandleOfTheMovedGate()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		GateRenderedElement handle = await host.FindButtonAsync("Move Build");
		Assert.NotNull(handle.ReferenceId);

		await host.PressKeyAsync(handle, "ArrowDown");

		(string Identifier, object?[] Arguments) call = Assert.Single(browser.Calls, call => call.Identifier == GateStubJsRuntime.FocusIdentifier);
		Assert.Equal(handle.ReferenceId, Assert.IsType<ElementReference>(call.Arguments[0]).Id);
	}

	[Fact]
	public async Task AfterAMoveByKeyTheNextKeyMovesTheSameGateAgainBecauseItsHandleIsTheOneFocused()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database, jsRuntime: browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.PressKeyAsync(await host.FindButtonAsync("Move Lint"), "ArrowUp");
		await host.PressKeyAsync(await host.FindButtonAsync("Move Lint"), "ArrowUp");

		Assert.Equal("Lint,Build,Tests", await GateTestData.ReadNamesAsync(database));
		Assert.Equal(2, browser.Calls.Count(call => call.Identifier == GateStubJsRuntime.FocusIdentifier));
	}

	[Fact]
	public async Task MovingAGateThatWasDeletedMeanwhileShowsTheMessageAndRefreshesTheList()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await new GateService(database.ContextFactory).DeleteAsync(gates["Tests"].Id, TestContext.Current.CancellationToken);

		await host.PressKeyAsync(await host.FindButtonAsync("Move Tests"), "ArrowUp");

		Assert.Equal(["Gate not found."], await host.AlertsAsync());
		Assert.Equal(["Build"], await host.TextsWithClassAsync("gate-text"));
	}

	[Fact]
	public async Task DroppingAHandleOnALaterGateRunsTheGateInThatPlace()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.DragAsync(await host.FindButtonAsync("Move Build"), RowAt(2));

		Assert.Equal("Tests,Lint,Build", await GateTestData.ReadNamesAsync(database));
		Assert.Equal(["Tests", "Lint", "Build"], await host.TextsWithClassAsync("gate-text"));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task DroppingAHandleOnAnEarlierGateRunsTheGateInThatPlace()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.DragAsync(await host.FindButtonAsync("Move Lint"), RowAt(0));

		Assert.Equal("Lint,Build,Tests", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task DroppingAHandleOnItsOwnGateMovesNothing()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		await host.DragAsync(await host.FindButtonAsync("Move Tests"), RowAt(1));

		Assert.Equal("Build,Tests,Lint", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task ADropWithNoDragInProgressMovesNothing()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		IReadOnlyList<GateRenderedElement> rows = await host.FindElementsAsync("li");

		await host.DropOnAsync(rows[2]);

		Assert.Equal("Build,Tests,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Empty(await host.AlertsAsync());
	}

	[Fact]
	public async Task ADragThatEndedWithoutADropForgetsTheGateThatWasDragged()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		GateRenderedElement handle = await host.FindButtonAsync("Move Build");
		await host.StartDragAsync(handle);
		await host.EndDragAsync(handle);

		await host.DropOnAsync((await host.FindElementsAsync("li"))[2]);

		Assert.Equal("Build,Tests,Lint", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task WhileAGateIsDraggedTheGateItIsOverShowsOnWhichSideItWouldStand()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		Assert.Equal(["gate-row", "gate-row", "gate-row"], (await host.FindElementsAsync("li")).Select(row => row.Attribute("class")));

		await host.StartDragAsync(await host.FindButtonAsync("Move Tests"));
		await host.DragEnterAsync((await host.FindElementsAsync("li"))[2]);
		Assert.Equal(["gate-row", "gate-row", "gate-row gate-drop-below"], (await host.FindElementsAsync("li")).Select(row => row.Attribute("class")));

		await host.DragEnterAsync((await host.FindElementsAsync("li"))[0]);
		Assert.Equal(["gate-row gate-drop-above", "gate-row", "gate-row"], (await host.FindElementsAsync("li")).Select(row => row.Attribute("class")));

		await host.DragEnterAsync((await host.FindElementsAsync("li"))[1]);
		Assert.Equal(["gate-row", "gate-row", "gate-row"], (await host.FindElementsAsync("li")).Select(row => row.Attribute("class")));

		await host.DragEnterAsync((await host.FindElementsAsync("li"))[0]);
		await host.DropOnAsync((await host.FindElementsAsync("li"))[0]);
		Assert.Equal(["gate-row", "gate-row", "gate-row"], (await host.FindElementsAsync("li")).Select(row => row.Attribute("class")));
	}

	[Fact]
	public async Task OnlyTheHandlesAreDraggableAndOnlyTheRowsTakeADrop()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		IReadOnlyList<GateRenderedElement> draggable = [.. (await host.ElementsAsync()).Where(element => element.Attribute("draggable") == "true")];

		Assert.Equal(["Move Build", "Move Tests"], draggable.Select(element => element.Attribute("aria-label")));
		Assert.All(await host.FindElementsAsync("li"), row => Assert.True(row.EventHandlers.ContainsKey("ondrop"), "Every row should take a drop."));
	}

	// The row at a place in the list, picked from the elements a host shows.
	private static Func<IReadOnlyList<GateRenderedElement>, GateRenderedElement> RowAt(int index)
	{
		return elements => elements.Where(element => element.Name == "li").ElementAt(index);
	}
}
