using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Once an agent has picked a card up, people can no longer edit or delete it; the service refuses and leaves the card untouched.</summary>
public sealed class CardServiceLockTests
{
	[Theory]
	[InlineData(CardLane.InProgress, "in In Progress")]
	[InlineData(CardLane.Gates, "in Gates")]
	[InlineData(CardLane.Review, "in Review")]
	[InlineData(CardLane.Complete, "in Complete")]
	[InlineData(CardLane.Closed, "closed")]
	public async Task UpdateAsync_RefusesACardOutsideTheQueueAndChangesNothing(CardLane lane, string where)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddLaneAsync(database, CardLane.Queue, "Q1", "Q2");
		WorkCard card = await TestData.AddCardAsync(database, "Picked up", lane, PyxieSeedData.BugCategoryId, agent.Id, agent.Id, "Original text");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Renamed", PyxieSeedData.ChoreCategoryId, description: "New text"), TestContext.Current.CancellationToken));

		Assert.Equal($"This card is {where} and can no longer be edited.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 3);
		Assert.Equal("Q1,Q2", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
	}

	[Theory]
	[InlineData(CardLane.InProgress, "in In Progress")]
	[InlineData(CardLane.Gates, "in Gates")]
	[InlineData(CardLane.Review, "in Review")]
	[InlineData(CardLane.Complete, "in Complete")]
	[InlineData(CardLane.Closed, "closed")]
	public async Task DeleteAsync_RefusesACardOutsideTheQueueAndChangesNothing(CardLane lane, string where)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		WorkCard first = await TestData.AddCardAsync(database, "First", lane, workerAgentId: agent.Id);
		WorkCard second = await TestData.AddCardAsync(database, "Second", lane, reviewerAgentId: agent.Id);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(first.Id, TestContext.Current.CancellationToken));

		Assert.Equal($"This card is {where} and can no longer be deleted.", message);
		await CardAssert.UnchangedAsync(database, first, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, second, expectedCardCount: 2);
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(database, lane));
	}

	[Fact]
	public async Task UpdateAsync_ReportsTheLockBeforeAnyFieldProblem()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Picked up", CardLane.InProgress);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, new CardInput(" ", " ", 0, null, null), TestContext.Current.CancellationToken));

		Assert.Equal("This card is in In Progress and can no longer be edited.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_StillEditsACardInTheQueueWhileOtherCardsAreLocked()
	{
		await using TestDatabase database = new();
		DateTime arranged = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		WorkCard queued = await TestData.AddCardAsync(database, "Queued", stampedUtc: arranged);
		WorkCard working = await TestData.AddCardAsync(database, "Working", CardLane.InProgress);
		CardService service = new(database.ContextFactory, database.Clock);
		DateTime updatedAt = database.Clock.Tick();

		WorkCard updated = ResultAssert.Succeeded(await service.UpdateAsync(queued.Id, await TestCardInput.CreateAsync(database, "Queued, revised", PyxieSeedData.BugCategoryId, description: "Revised"), TestContext.Current.CancellationToken));

		Assert.Equal("Queued, revised", updated.Name);
		WorkCard? stored = await TestData.FindCardAsync(database, queued.Id);
		Assert.NotNull(stored);
		Assert.Equal("Queued, revised", stored.Name);
		Assert.Equal("Revised", stored.Description);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(updatedAt, stored.UpdatedUtc);
		await CardAssert.UnchangedAsync(database, working, expectedCardCount: 2);
	}

	[Fact]
	public async Task DeleteAsync_StillDeletesACardInTheQueueAndResequencesItWhileOtherLanesAreLocked()
	{
		await using TestDatabase database = new();
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(database, CardLane.Queue, "A", "B", "C");
		WorkCard working = await TestData.AddCardAsync(database, "Working", CardLane.InProgress);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(queue["A"].Id, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindCardAsync(database, queue["A"].Id));
		Assert.Equal("B,C", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Queue));
		await CardAssert.UnchangedAsync(database, working, expectedCardCount: 3);
	}

	[Fact]
	public async Task UpdateAsync_OfAnUnknownCard_ReportsNotFound()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(999, TestCardInput.Create("Card"), TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
	}

	[Fact]
	public async Task DeleteAsync_OfAnUnknownCard_ReportsNotFound()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.DeleteAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
	}
}
