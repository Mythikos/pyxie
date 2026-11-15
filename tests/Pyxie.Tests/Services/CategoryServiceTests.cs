using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CategoryServiceTests
{
	[Fact]
	public async Task ListAsync_ReturnsSeededCategoriesOrderedByName()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		IReadOnlyList<Category> categories = await service.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Bug", "Chore", "Feature"], categories.Select(category => category.Name));
	}

	[Fact]
	public async Task ListAsync_OrdersNewCategoriesAmongSeededOnes()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.CreateAsync("Docs", token));
		ResultAssert.Succeeded(await service.CreateAsync("Alpha", token));

		IReadOnlyList<Category> categories = await service.ListAsync(token);

		Assert.Equal(["Alpha", "Bug", "Chore", "Docs", "Feature"], categories.Select(category => category.Name));
	}

	[Fact]
	public async Task CreateAsync_PersistsCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		Category created = ResultAssert.Succeeded(await service.CreateAsync("Research", TestContext.Current.CancellationToken));

		Category? stored = await TestData.FindCategoryAsync(database, created.Id);
		Assert.NotNull(stored);
		Assert.Equal("Research", stored.Name);
		Assert.NotEqual(PyxieSeedData.FeatureCategoryId, created.Id);
		Assert.NotEqual(PyxieSeedData.BugCategoryId, created.Id);
		Assert.NotEqual(PyxieSeedData.ChoreCategoryId, created.Id);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public async Task CreateAsync_RejectsBlankName(string name)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.CreateAsync(name, TestContext.Current.CancellationToken));

		Assert.Equal(3, (await service.ListAsync(TestContext.Current.CancellationToken)).Count);
	}

	[Fact]
	public async Task CreateAsync_RejectsNameLongerThanLimit()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.CreateAsync(new string('c', PyxieLimits.MaxNameLength + 1), token));
		ResultAssert.Succeeded(await service.CreateAsync(new string('c', PyxieLimits.MaxNameLength), token));

		Assert.Equal(4, (await service.ListAsync(token)).Count);
	}

	[Theory]
	[InlineData("Feature")]
	[InlineData("feature")]
	[InlineData("FEATURE")]
	[InlineData("fEaTuRe")]
	public async Task CreateAsync_RejectsDuplicateNameIgnoringCase(string name)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.CreateAsync(name, TestContext.Current.CancellationToken));

		Assert.Equal(3, (await service.ListAsync(TestContext.Current.CancellationToken)).Count);
	}

	[Fact]
	public async Task CreateAsync_RejectsDuplicateOfNewlyCreatedCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.CreateAsync("Research", token));

		ResultAssert.Failed(await service.CreateAsync("RESEARCH", token));
	}

	[Fact]
	public async Task RenameAsync_ChangesName()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		Category renamed = ResultAssert.Succeeded(await service.RenameAsync(PyxieSeedData.ChoreCategoryId, "Maintenance", TestContext.Current.CancellationToken));

		Assert.Equal(PyxieSeedData.ChoreCategoryId, renamed.Id);
		Category? stored = await TestData.FindCategoryAsync(database, PyxieSeedData.ChoreCategoryId);
		Assert.NotNull(stored);
		Assert.Equal("Maintenance", stored.Name);
	}

	[Theory]
	[InlineData("bug")]
	[InlineData("BUG")]
	[InlineData("Bug")]
	public async Task RenameAsync_AllowsChangingOnlyTheCaseOfOwnName(string name)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.RenameAsync(PyxieSeedData.BugCategoryId, name, TestContext.Current.CancellationToken));

		Category? stored = await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId);
		Assert.NotNull(stored);
		Assert.Equal(name, stored.Name);
	}

	[Theory]
	[InlineData("Feature")]
	[InlineData("feature")]
	[InlineData("CHORE")]
	public async Task RenameAsync_RejectsNameUsedByAnotherCategory(string name)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.RenameAsync(PyxieSeedData.BugCategoryId, name, TestContext.Current.CancellationToken));

		Category? stored = await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId);
		Assert.NotNull(stored);
		Assert.Equal("Bug", stored.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	public async Task RenameAsync_RejectsBlankName(string name)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.RenameAsync(PyxieSeedData.BugCategoryId, name, TestContext.Current.CancellationToken));

		Category? stored = await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId);
		Assert.NotNull(stored);
		Assert.Equal("Bug", stored.Name);
	}

	[Fact]
	public async Task RenameAsync_RejectsNameLongerThanLimit()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.RenameAsync(PyxieSeedData.BugCategoryId, new string('c', PyxieLimits.MaxNameLength + 1), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task RenameAsync_RejectsUnknownCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.RenameAsync(999, "Whatever", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeleteAsync_RemovesUnusedCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.DeleteAsync(PyxieSeedData.ChoreCategoryId, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindCategoryAsync(database, PyxieSeedData.ChoreCategoryId));
		Assert.Equal(["Bug", "Feature"], (await service.ListAsync(TestContext.Current.CancellationToken)).Select(category => category.Name));
	}

	[Fact]
	public async Task DeleteAsync_RefusesCategoryUsedByCards_AndReportsCount()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "One", categoryId: PyxieSeedData.BugCategoryId);
		await TestData.AddCardAsync(database, "Two", CardLane.Review, categoryId: PyxieSeedData.BugCategoryId);
		await TestData.AddCardAsync(database, "Other", categoryId: PyxieSeedData.FeatureCategoryId);
		CategoryService service = new(database.ContextFactory);

		string message = ResultAssert.Failed(await service.DeleteAsync(PyxieSeedData.BugCategoryId, TestContext.Current.CancellationToken));

		Assert.Contains("2", message, StringComparison.Ordinal);
		Assert.NotNull(await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId));
		Assert.Equal(3, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_RefusesCategoryUsedOnlyByAClosedCard()
	{
		await using TestDatabase database = new();
		WorkCard finished = await TestData.AddCardAsync(database, "Finished", CardLane.Complete, categoryId: PyxieSeedData.BugCategoryId);
		CardService cards = new(database.ContextFactory, database.Clock);
		CategoryService categories = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await cards.CloseAsync(finished.Id, token));

		string message = ResultAssert.Failed(await categories.DeleteAsync(PyxieSeedData.BugCategoryId, token));

		Assert.Contains("1 card", message, StringComparison.Ordinal);
		Assert.NotNull(await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId));
		Assert.Equal(1, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_AllowsCategoryAfterItsCardsAreDeleted()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "One", categoryId: PyxieSeedData.BugCategoryId);
		CategoryService categories = new(database.ContextFactory);
		CardService cards = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Failed(await categories.DeleteAsync(PyxieSeedData.BugCategoryId, token));

		ResultAssert.Succeeded(await cards.DeleteAsync(card.Id, token));
		ResultAssert.Succeeded(await categories.DeleteAsync(PyxieSeedData.BugCategoryId, token));

		Assert.Null(await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId));
	}

	[Fact]
	public async Task DeleteAsync_RejectsUnknownCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.DeleteAsync(999, TestContext.Current.CancellationToken));
	}
}
