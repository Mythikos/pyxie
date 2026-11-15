using System.Globalization;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CardServiceRequiredFieldsTests
{
	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t\r\n")]
	public async Task CreateAsync_RejectsBlankName(string name)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create(name), TestContext.Current.CancellationToken));

		Assert.Equal("Name is required.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t\r\n")]
	public async Task CreateAsync_RejectsBlankDescription(string description)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Card", description: description), TestContext.Current.CancellationToken));

		Assert.Equal("Description is required.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task CreateAsync_RejectsMissingCategory(int categoryId)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Card", categoryId: categoryId), TestContext.Current.CancellationToken));

		Assert.Equal("Category is required.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_RejectsUnknownCategory()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", categoryId: 999), TestContext.Current.CancellationToken));

		Assert.Equal("Category not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task CreateAsync_RejectsMissingWorker(int? workerAgentId)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Card", workerAgentId: workerAgentId), TestContext.Current.CancellationToken));

		Assert.Equal("Worker is required.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_RejectsOverLimitDescriptionWithTheLimitInTheMessage()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Card", description: new string('d', PyxieLimits.MaxDescriptionLength + 1)), TestContext.Current.CancellationToken));

		Assert.Contains(PyxieLimits.MaxDescriptionLength.ToString(CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_ReportsNameBeforeDescriptionBeforeCategoryBeforeWorkerBeforeAttempts()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		string allMissing = ResultAssert.Failed(await service.CreateAsync(new CardInput(" ", " ", 0, null, null, AttemptLimit: 0), token));
		string descriptionCategoryWorkerAndAttemptsMissing = ResultAssert.Failed(await service.CreateAsync(new CardInput("Card", " ", 0, null, null, AttemptLimit: 0), token));
		string categoryWorkerAndAttemptsMissing = ResultAssert.Failed(await service.CreateAsync(new CardInput("Card", "Text", 0, null, null, AttemptLimit: 0), token));
		string workerAndAttemptsMissing = ResultAssert.Failed(await service.CreateAsync(new CardInput("Card", "Text", 1, null, null, AttemptLimit: 0), token));
		string attemptsMissing = ResultAssert.Failed(await service.CreateAsync(new CardInput("Card", "Text", 1, 1, null, AttemptLimit: 0), token));

		Assert.Equal("Name is required.", allMissing);
		Assert.Equal("Description is required.", descriptionCategoryWorkerAndAttemptsMissing);
		Assert.Equal("Category is required.", categoryWorkerAndAttemptsMissing);
		Assert.Equal("Worker is required.", workerAndAttemptsMissing);
		Assert.Equal("Attempts must be between 1 and 10.", attemptsMissing);
	}

	[Fact]
	public async Task CreateAsync_TrimsNameAndDescription()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard created = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "  Card  ", description: "\n  Text  \n"), TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, created.Id);
		Assert.NotNull(stored);
		Assert.Equal("Card", stored.Name);
		Assert.Equal("Text", stored.Description);
	}

	[Fact]
	public async Task CreateAsync_ReturnsCardWithItsCategoryLoaded()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard created = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", categoryId: PyxieSeedData.ChoreCategoryId), TestContext.Current.CancellationToken));

		Assert.Equal("Chore", created.Category.Name);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(PyxieLimits.MaxAttemptLimit)]
	public async Task CreateAsync_StoresTheAttemptLimitItWasGiven(int attemptLimit)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard created = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", attemptLimit: attemptLimit), TestContext.Current.CancellationToken));

		Assert.Equal(attemptLimit, created.AttemptLimit);
		Assert.Equal(attemptLimit, (await TestData.FindCardAsync(database, created.Id))?.AttemptLimit);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(PyxieLimits.MaxAttemptLimit + 1)]
	public async Task CreateAsync_RejectsAnAttemptLimitOutsideOneToTen(int attemptLimit)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", attemptLimit: attemptLimit), TestContext.Current.CancellationToken));

		Assert.Equal("Attempts must be between 1 and 10.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_GivesACardBuiltWithoutAnAttemptLimitTheDefaultOfFive()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard created = ResultAssert.Succeeded(await service.CreateAsync(new CardInput("Card", "Text", PyxieSeedData.FeatureCategoryId, worker.Id, null), TestContext.Current.CancellationToken));

		Assert.Equal(5, created.AttemptLimit);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task UpdateAsync_RejectsBlankNameAndLeavesCardUnchanged(string name)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create(name, description: "Changed"), TestContext.Current.CancellationToken));

		Assert.Equal("Name is required.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public async Task UpdateAsync_RejectsBlankDescriptionAndLeavesCardUnchanged(string description)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Renamed", description: description), TestContext.Current.CancellationToken));

		Assert.Equal("Description is required.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task UpdateAsync_RejectsMissingCategoryAndLeavesCardUnchanged(int categoryId)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Renamed", categoryId: categoryId), TestContext.Current.CancellationToken));

		Assert.Equal("Category is required.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_RejectsUnknownCategoryAndLeavesCardUnchanged()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Renamed", categoryId: 999), TestContext.Current.CancellationToken));

		Assert.Equal("Category not found.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_RejectsOverLimitNameAndDescriptionAndLeavesCardUnchanged()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		string nameMessage = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, new string('n', PyxieLimits.MaxNameLength + 1)), token));
		string descriptionMessage = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Renamed", description: new string('d', PyxieLimits.MaxDescriptionLength + 1)), token));

		Assert.Equal($"Name must be {PyxieLimits.MaxNameLength} characters or fewer.", nameMessage);
		Assert.Equal($"Description must be {PyxieLimits.MaxDescriptionLength} characters or fewer.", descriptionMessage);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(PyxieLimits.MaxAttemptLimit + 1)]
	public async Task UpdateAsync_RejectsAnAttemptLimitOutsideOneToTenAndLeavesCardUnchanged(int attemptLimit)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Original", attemptLimit: attemptLimit), TestContext.Current.CancellationToken));

		Assert.Equal("Attempts must be between 1 and 10.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_ChangesTheAttemptLimit()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original", attemptLimit: 3);
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard updated = ResultAssert.Succeeded(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Original", attemptLimit: 7), TestContext.Current.CancellationToken));

		Assert.Equal(7, updated.AttemptLimit);
		Assert.Equal(7, (await TestData.FindCardAsync(database, card.Id))?.AttemptLimit);
	}

	[Fact]
	public async Task UpdateAsync_ChangesCategory()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original", categoryId: PyxieSeedData.FeatureCategoryId);
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard updated = ResultAssert.Succeeded(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Original", categoryId: PyxieSeedData.BugCategoryId), TestContext.Current.CancellationToken));

		Assert.Equal("Bug", updated.Category.Name);
		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(PyxieSeedData.BugCategoryId, stored.CategoryId);
	}

	[Fact]
	public async Task UpdateAsync_RequiresADescriptionForACardStoredWithoutOne()
	{
		await using TestDatabase database = new();
		WorkCard legacy = await TestData.AddCardAsync(database, "Legacy", description: string.Empty);
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.UpdateAsync(legacy.Id, TestCardInput.Create("Legacy", description: string.Empty), token));
		ResultAssert.Succeeded(await service.UpdateAsync(legacy.Id, await TestCardInput.CreateAsync(database, "Legacy", description: "Now described"), token));

		WorkCard? stored = await TestData.FindCardAsync(database, legacy.Id);
		Assert.NotNull(stored);
		Assert.Equal("Now described", stored.Description);
	}

	[Fact]
	public async Task ClaimNextAndDeleteAsync_StillWorkForACardStoredWithoutADescription()
	{
		await using TestDatabase database = new();
		TestData.ConnectRepository(database);
		WorkCard movable = await TestData.AddCardAsync(database, "Movable", description: string.Empty);
		WorkCard deletable = await TestData.AddCardAsync(database, "Deletable", description: string.Empty);
		CardService service = new(database.ContextFactory, database.Clock);
		CardPipelineService pipeline = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		PipelineWork? claimed = await pipeline.ClaimNextAsync(token);
		Assert.NotNull(claimed);
		Assert.Equal(movable.Id, claimed.CardId);
		Assert.Equal("Movable", await TestData.ReadLaneNamesAsync(database, CardLane.InProgress));

		ResultAssert.Succeeded(await service.DeleteAsync(deletable.Id, token));
		Assert.Null(await TestData.FindCardAsync(database, deletable.Id));
		Assert.Equal(1, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CategoryDelete_IsRefusedWhileACardCreatedByTheServiceUsesIt()
	{
		await using TestDatabase database = new();
		CardService cards = new(database.ContextFactory, database.Clock);
		CategoryService categories = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;
		WorkCard card = ResultAssert.Succeeded(await cards.CreateAsync(await TestCardInput.CreateAsync(database, "Card", categoryId: PyxieSeedData.BugCategoryId), token));

		ResultAssert.Failed(await categories.DeleteAsync(PyxieSeedData.BugCategoryId, token));
		Assert.NotNull(await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId));

		ResultAssert.Succeeded(await cards.DeleteAsync(card.Id, token));
		ResultAssert.Succeeded(await categories.DeleteAsync(PyxieSeedData.BugCategoryId, token));
	}
}
