using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Pages;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Retrying or closing a card from its tile takes the pressed button off the board, so the keyboard focus is sent on: to the name button of the retried card, to the tile that took the closed card's place, or to the lane's heading when the lane has nothing left. A refused action leaves the focus where it was.</summary>
public sealed class BoardFocusTests
{
	[Fact]
	public async Task Retry_MovesTheFocusToTheNameButtonOfTheRetriedCard()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "First card");
		WorkCard stopped = await AddStoppedCardAsync(database, "Stopped card");
		await TestData.AddCardAsync(database, "Last card");
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement name = await NameButtonAsync(host, "Stopped card");

		await host.ClickAsync(await RetryButtonAsync(host));

		Assert.Equal(CardState.Ready, (await ReadAsync(database, stopped.Id)).State);
		Assert.Equal(name.ReferenceId, await FocusedIdAsync(browser));
		Assert.Single(FocusCalls(browser));
		Assert.DoesNotContain(await host.ElementsAsync(), element => ClassesOf(element).Contains("card-retry"));
	}

	[Fact]
	public async Task Close_MovesTheFocusToTheNameButtonOfTheNextCardInTheLane()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Done one", CardLane.Complete, attempt: 1);
		WorkCard closed = await TestData.AddCardAsync(database, "Done two", CardLane.Complete, attempt: 1);
		await TestData.AddCardAsync(database, "Done three", CardLane.Complete, attempt: 1);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement next = await NameButtonAsync(host, "Done three");

		await host.ClickAsync(await CloseButtonAsync(host, "Done two"));

		Assert.Equal(CardLane.Closed, (await ReadAsync(database, closed.Id)).Lane);
		Assert.Equal(next.ReferenceId, await FocusedIdAsync(browser));
		Assert.Single(FocusCalls(browser));
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Text == "Done two");
	}

	[Fact]
	public async Task Close_OfTheLastCardInTheLaneMovesTheFocusToThePreviousCard()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Done one", CardLane.Complete, attempt: 1);
		await TestData.AddCardAsync(database, "Done two", CardLane.Complete, attempt: 1);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement previous = await NameButtonAsync(host, "Done one");

		await host.ClickAsync(await CloseButtonAsync(host, "Done two"));

		Assert.Equal(previous.ReferenceId, await FocusedIdAsync(browser));
		Assert.Single(FocusCalls(browser));
	}

	[Fact]
	public async Task Close_OfTheOnlyCardInTheLaneMovesTheFocusToTheLanesHeading()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued card");
		await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement heading = await HeadingAsync(host, "Complete");
		Assert.Equal("-1", heading.Attribute("tabindex"));

		await host.ClickAsync(await CloseButtonAsync(host, "Done card"));

		Assert.Equal(heading.ReferenceId, await FocusedIdAsync(browser));
		Assert.Single(FocusCalls(browser));
	}

	[Fact]
	public async Task Close_WaitsForTheBoardToBeReadAgainBeforeMovingTheFocus()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Done one", CardLane.Complete, attempt: 1);
		await TestData.AddCardAsync(database, "Done two", CardLane.Complete, attempt: 1);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		CallProbe reads = services.GetRequiredService<GatedCardService>().BoardReads;
		int next = reads.Started + 1;
		reads.Hold(next);
		GateRenderedElement last = await NameButtonAsync(host, "Done two");
		GateRenderedElement previous = await NameButtonAsync(host, "Done one");

		await host.ClickAsync(await CloseButtonAsync(host, "Done one"));
		await reads.WaitForStartAsync(next);

		Assert.Empty(FocusCalls(browser));

		reads.Release(next);

		Assert.Equal(last.ReferenceId, await FocusedIdAsync(browser));
		Assert.NotEqual(previous.ReferenceId, last.ReferenceId);
	}

	[Fact]
	public async Task Close_LeavesTheFocusInTheLaneOfTheClosedCardWhenAnotherLaneHasCards()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued card");
		await TestData.AddCardAsync(database, "Working card", CardLane.InProgress, attempt: 1);
		await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement heading = await HeadingAsync(host, "Complete");

		await host.ClickAsync(await CloseButtonAsync(host, "Done card"));

		Assert.Equal(heading.ReferenceId, await FocusedIdAsync(browser));
	}

	[Fact]
	public async Task Retry_OfACardSomeoneElseAlreadyRetried_ShowsTheRefusalAndFocusesNothing()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await AddStoppedCardAsync(database, "Stopped card");
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement retry = await RetryButtonAsync(host);
		ResultAssert.Succeeded(await new CardService(database.ContextFactory, database.Clock).RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		await host.ClickAsync(retry);

		Assert.Single(await host.AlertsAsync());
		Assert.Empty(FocusCalls(browser));
	}

	[Fact]
	public async Task Close_OfACardThatWasAlreadyClosed_ShowsTheRefusalAndFocusesNothing()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);
		GateStubJsRuntime browser = new();
		await using ServiceProvider services = BoardServices.Create(database, browser);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		GateRenderedElement close = await CloseButtonAsync(host, "Done card");
		ResultAssert.Succeeded(await new CardService(database.ContextFactory, database.Clock).CloseAsync(card.Id, TestContext.Current.CancellationToken));

		await host.ClickAsync(close);

		Assert.Single(await host.AlertsAsync());
		Assert.Empty(FocusCalls(browser));
	}

	private static async Task<WorkCard> AddStoppedCardAsync(TestDatabase database, string name)
	{
		WorkCard card = await TestData.AddCardAsync(database, name, state: CardState.Error, attempt: 1);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Blocked, "Claude", "The task is unclear.");

		return card;
	}

	private static async Task<WorkCard> ReadAsync(TestDatabase database, int id)
	{
		return await TestData.FindCardAsync(database, id) ?? throw new InvalidOperationException($"Card {id} is gone.");
	}

	private static string[] ClassesOf(GateRenderedElement element)
	{
		return (element.Attribute("class") ?? string.Empty).Split(' ');
	}

	private static bool IsNameButton(GateRenderedElement element, string cardName)
	{
		return element.Name == "button" && ClassesOf(element).Contains("card-name") && element.Text == cardName;
	}

	private static async Task<GateRenderedElement> NameButtonAsync(GateInteractiveHost host, string cardName)
	{
		GateRenderedElement name = Assert.Single(await host.FindElementsAsync("button", element => IsNameButton(element, cardName)));
		Assert.NotNull(name.ReferenceId);

		return name;
	}

	private static async Task<GateRenderedElement> HeadingAsync(GateInteractiveHost host, string laneName)
	{
		GateRenderedElement heading = Assert.Single(await host.FindElementsAsync("h2", element => ClassesOf(element).Contains("lane-title") && element.Text == laneName));
		Assert.NotNull(heading.ReferenceId);

		return heading;
	}

	private static async Task<GateRenderedElement> RetryButtonAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("button", element => ClassesOf(element).Contains("card-retry")));
	}

	// A Complete card's Close button follows the card's name button in the markup, before the next card's.
	private static async Task<GateRenderedElement> CloseButtonAsync(GateInteractiveHost host, string cardName)
	{
		List<GateRenderedElement> elements = [.. await host.ElementsAsync()];
		int name = elements.FindIndex(element => IsNameButton(element, cardName));
		Assert.True(name >= 0, $"The board should show the card {cardName}.");

		return elements.Skip(name + 1).First(element => element.Name == "button" && element.Text == "Close");
	}

	private static List<(string Identifier, object?[] Arguments)> FocusCalls(GateStubJsRuntime browser)
	{
		return [.. browser.Calls.Where(call => call.Identifier == GateStubJsRuntime.FocusIdentifier)];
	}

	// The board reads itself again after the change before it knows where the focus goes, on its own context, so the test waits for the call to the browser.
	private static async Task<string?> FocusedIdAsync(GateStubJsRuntime browser)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(RuntimeTestTimeouts.Wait);

		while (FocusCalls(browser).Count == 0)
		{
			try
			{
				await Task.Delay(TimeSpan.FromMilliseconds(5), deadline.Token);
			}
			catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
			{
				Assert.Fail("The board should have asked the browser to focus an element once it was read again.");
			}
		}

		return Assert.IsType<ElementReference>(FocusCalls(browser).Last().Arguments[0]).Id;
	}
}
