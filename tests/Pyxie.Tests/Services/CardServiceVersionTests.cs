using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Every change a person makes to a card advances its version, so a pipeline operation that read the card earlier notices and reads again.</summary>
public sealed class CardServiceVersionTests
{
	[Fact]
	public async Task CreateAsync_StartsTheCardAtVersionZero()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard card = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card"), TestContext.Current.CancellationToken));

		Assert.Equal(0, card.Version);
		Assert.Equal(0, (await TestData.FindCardAsync(database, card.Id))?.Version);
	}

	[Fact]
	public async Task UpdateAsync_IncrementsTheVersionByOneEachTime()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		WorkCard first = ResultAssert.Succeeded(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Renamed once"), token));
		WorkCard second = ResultAssert.Succeeded(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Renamed twice"), token));

		Assert.Equal(card.Version + 1, first.Version);
		Assert.Equal(card.Version + 2, second.Version);
		Assert.Equal(card.Version + 2, (await TestData.FindCardAsync(database, card.Id))?.Version);
	}

	[Fact]
	public async Task UpdateAsync_WithAnUnchangedCard_StillIncrementsTheVersion()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", description: "Text");
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard updated = ResultAssert.Succeeded(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Card", description: "Text"), TestContext.Current.CancellationToken));

		Assert.Equal(card.Version + 1, updated.Version);
	}

	[Fact]
	public async Task CloseAsync_IncrementsTheVersion()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.Complete);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.CloseAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal(card.Version + 1, (await TestData.FindCardAsync(database, card.Id))?.Version);
	}

	[Fact]
	public async Task CloseAsync_LeavesTheVersionsOfTheOtherCompleteCardsAlone()
	{
		await using TestDatabase database = new();
		Dictionary<string, WorkCard> complete = await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C2", "C3");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.CloseAsync(complete["C1"].Id, TestContext.Current.CancellationToken));

		Assert.Equal(complete["C2"].Version, (await TestData.FindCardAsync(database, complete["C2"].Id))?.Version);
		Assert.Equal(complete["C3"].Version, (await TestData.FindCardAsync(database, complete["C3"].Id))?.Version);
	}

	[Theory]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	[InlineData(CardLane.Complete)]
	[InlineData(CardLane.Closed)]
	public async Task UpdateAsync_RefusedBecauseOfTheLane_LeavesTheVersionAlone(CardLane lane)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Renamed"), TestContext.Current.CancellationToken));

		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	[InlineData(CardLane.Closed)]
	public async Task CloseAsync_RefusedBecauseOfTheLane_LeavesTheVersionAlone(CardLane lane)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.CloseAsync(card.Id, TestContext.Current.CancellationToken));

		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_RefusedForInvalidFields_LeavesTheVersionAlone()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, " "), TestContext.Current.CancellationToken));

		Assert.Equal("Name is required.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}
}
