using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Closing out a completed card removes it from the board and keeps it in the database as history.</summary>
public sealed class CardServiceCloseTests
{
	[Fact]
	public async Task CloseAsync_KeepsTheCardInTheDatabaseAsClosedAndStampsItInUtc()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork: true, canReview: true);
		await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C3");
		WorkCard finished = await TestData.AddCardAsync(database, "Finished", CardLane.Complete, PyxieSeedData.BugCategoryId, agent.Id, agent.Id, "Done and dusted");
		CardService service = new(database.ContextFactory, database.Clock);
		DateTime closedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await service.CloseAsync(finished.Id, TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, finished.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Closed, stored.Lane);
		Assert.NotNull(stored.ClosedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.ClosedUtc.Value.Kind);
		Assert.Equal(closedAt, stored.ClosedUtc.Value);
		Assert.Equal(stored.ClosedUtc, stored.UpdatedUtc);
		Assert.Equal(finished.CreatedUtc, stored.CreatedUtc);
		Assert.Equal("Finished", stored.Name);
		Assert.Equal("Done and dusted", stored.Description);
		Assert.Equal(PyxieSeedData.BugCategoryId, stored.CategoryId);
		Assert.Equal(agent.Id, stored.WorkerAgentId);
		Assert.Equal(agent.Id, stored.ReviewerAgentId);
		Assert.Equal(3, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CloseAsync_RemovesTheCardFromTheBoardAndLeavesOtherLanesAlone()
	{
		await using TestDatabase database = new();
		await TestData.AddLaneAsync(database, CardLane.Queue, "Q1", "Q2");
		await TestData.AddLaneAsync(database, CardLane.InProgress, "I1");
		await TestData.AddLaneAsync(database, CardLane.Gates, "G1");
		await TestData.AddLaneAsync(database, CardLane.Review, "R1");
		Dictionary<string, WorkCard> complete = await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C2", "C3");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.CloseAsync(complete["C2"].Id, TestContext.Current.CancellationToken));

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);
		Assert.Equal(["Q1", "Q2", "I1", "G1", "R1", "C1", "C3"], board.Select(entry => entry.Card.Name));
		Assert.Equal("Q1,Q2", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Queue));
		Assert.Equal("I1", await TestData.ReadLaneNamesAsync(database, CardLane.InProgress));
		Assert.Equal("G1", await TestData.ReadLaneNamesAsync(database, CardLane.Gates));
		Assert.Equal("R1", await TestData.ReadLaneNamesAsync(database, CardLane.Review));
		Assert.Equal(8, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData("C1", "C2,C3")]
	[InlineData("C2", "C1,C3")]
	[InlineData("C3", "C1,C2")]
	public async Task CloseAsync_ResequencesTheRemainingCompleteCardsWhicheverCardIsClosed(string closed, string expectedComplete)
	{
		await using TestDatabase database = new();
		Dictionary<string, WorkCard> complete = await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C2", "C3");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.CloseAsync(complete[closed].Id, TestContext.Current.CancellationToken));

		Assert.Equal(expectedComplete, await TestData.ReadLaneNamesAsync(database, CardLane.Complete));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Complete));
	}

	[Fact]
	public async Task CloseAsync_OfTheLastCompleteCard_LeavesTheLaneEmpty()
	{
		await using TestDatabase database = new();
		WorkCard only = await TestData.AddCardAsync(database, "Only", CardLane.Complete);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.CloseAsync(only.Id, TestContext.Current.CancellationToken));

		Assert.Empty(await TestData.ReadLaneAsync(database, CardLane.Complete));
		Assert.Empty(await service.GetBoardAsync(TestContext.Current.CancellationToken));
		Assert.Equal(1, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task CloseAsync_RefusesACardThatIsNotCompleteAndChangesNothing(CardLane lane)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Not finished", lane);
		WorkCard neighbour = await TestData.AddCardAsync(database, "Neighbour", lane);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CloseAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Only completed cards can be closed.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, neighbour, expectedCardCount: 2);
	}

	[Fact]
	public async Task CloseAsync_RefusesACardThatIsAlreadyClosedAndKeepsItsOriginalClosingTime()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Finished", CardLane.Complete);
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.CloseAsync(card.Id, token));
		WorkCard? firstClose = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(firstClose);

		string message = ResultAssert.Failed(await service.CloseAsync(card.Id, token));

		Assert.Equal("Card is already closed.", message);
		await CardAssert.UnchangedAsync(database, firstClose, expectedCardCount: 1);
	}

	[Fact]
	public async Task CloseAsync_ReportsACardArrangedAsClosedAsAlreadyClosedNotAsNotComplete()
	{
		await using TestDatabase database = new();
		WorkCard history = await TestData.AddCardAsync(database, "History", CardLane.Closed);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CloseAsync(history.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Card is already closed.", message);
		await CardAssert.UnchangedAsync(database, history, expectedCardCount: 1);
	}

	[Fact]
	public async Task CloseAsync_OfAnUnknownCard_ReportsNotFound()
	{
		await using TestDatabase database = new();
		await TestData.AddLaneAsync(database, CardLane.Complete, "C1");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CloseAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
		Assert.Equal("C1", await TestData.ReadLaneNamesAsync(database, CardLane.Complete));
	}

	[Fact]
	public async Task ACardClosedThroughTheServiceCanNoLongerBeEditedDeletedRetriedOrTakenByThePipeline()
	{
		await using TestDatabase database = new();
		TestData.ConnectRepository(database);
		WorkCard card = await TestData.AddCardAsync(database, "Finished", CardLane.Complete);
		CardService service = new(database.ContextFactory, database.Clock);
		CardPipelineService pipeline = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.CloseAsync(card.Id, token));
		WorkCard? closed = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(closed);

		ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Renamed"), token));
		ResultAssert.Failed(await service.DeleteAsync(card.Id, token));
		ResultAssert.Failed(await service.RetryAsync(card.Id, token));
		Assert.Null(await pipeline.ClaimNextAsync(token));

		await CardAssert.UnchangedAsync(database, closed, expectedCardCount: 1);
	}
}
