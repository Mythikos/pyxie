using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An agent can move a card between the moment the service checks its lane and the moment it writes; the write must then be refused, not applied to the card's new lane.</summary>
public sealed class CardServiceLaneRaceTests
{
	[Fact]
	public async Task UpdateAsync_RefusesACardThatAnAgentPicksUpAfterTheCheckAndLeavesItUntouched()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(database, CardLane.Queue, "A", "B");
		WorkCard card = queue["A"];
		CardService service = new(database.ContextFactory, database.Clock);
		CardInput input = await TestCardInput.CreateAsync(database, "Renamed", description: "New text");
		interceptor.Arm($"UPDATE \"Cards\" SET \"Lane\" = {(int)CardLane.InProgress}, \"SortOrder\" = 0 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, input, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal("This card is in In Progress and can no longer be edited.", message);
		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(card.Name, stored.Name);
		Assert.Equal(card.Description, stored.Description);
		Assert.Equal(card.UpdatedUtc, stored.UpdatedUtc);
	}

	[Fact]
	public async Task UpdateAsync_RefusesACardThatIsDeletedAfterTheCheck()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		WorkCard card = await TestData.AddCardAsync(database, "A");
		CardService service = new(database.ContextFactory, database.Clock);
		CardInput input = await TestCardInput.CreateAsync(database, "Renamed");
		interceptor.Arm($"DELETE FROM \"Cards\" WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, input, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal("Card not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_RefusesACardThatAnAgentPicksUpAfterTheCheckAndKeepsIt()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(database, CardLane.Queue, "A", "B", "C");
		WorkCard card = queue["B"];
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"UPDATE \"Cards\" SET \"Lane\" = {(int)CardLane.InProgress}, \"SortOrder\" = 0 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await service.DeleteAsync(card.Id, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal("This card is in In Progress and can no longer be deleted.", message);
		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal("A,C", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal(3, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_ReportsACardThatSomeoneElseDeletedAfterTheCheckAsNotFound()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(database, CardLane.Queue, "A", "B");
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"DELETE FROM \"Cards\" WHERE \"Id\" = {queue["A"].Id}");

		string message = ResultAssert.Failed(await service.DeleteAsync(queue["A"].Id, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal("Card not found.", message);
		Assert.Equal("B", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
	}

	[Fact]
	public async Task CloseAsync_RefusesACardThatIsNoLongerCompleteAfterTheCheckAndDoesNotCloseIt()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		Dictionary<string, WorkCard> complete = await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C2");
		WorkCard card = complete["C1"];
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"UPDATE \"Cards\" SET \"Lane\" = {(int)CardLane.Queue}, \"SortOrder\" = 0 WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await service.CloseAsync(card.Id, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal("Only completed cards can be closed.", message);
		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Null(stored.ClosedUtc);
		Assert.Equal("C2", await TestData.ReadLaneNamesAsync(database, CardLane.Complete));
	}

	[Fact]
	public async Task CloseAsync_ReportsACardThatSomeoneElseClosedAfterTheCheckAsAlreadyClosedAndKeepsTheFirstClosingTime()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		WorkCard card = await TestData.AddCardAsync(database, "C1", CardLane.Complete);
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"UPDATE \"Cards\" SET \"Lane\" = {(int)CardLane.Closed}, \"ClosedUtc\" = '2026-01-02 03:04:05' WHERE \"Id\" = {card.Id}");

		string message = ResultAssert.Failed(await service.CloseAsync(card.Id, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal("Card is already closed.", message);
		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), stored.ClosedUtc);
	}
}
