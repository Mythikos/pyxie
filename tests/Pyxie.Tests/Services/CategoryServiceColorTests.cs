using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A category has a color for its chip: the default categories come tinted, a new one is plain until a person chooses a color, and only a defined color can be chosen.</summary>
public sealed class CategoryServiceColorTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ListAsync_ReturnsTheSeededCategoriesInTheirColors()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		IReadOnlyList<Category> categories = await service.ListAsync(Token);

		Assert.Equal([("Bug", CategoryColor.Pink), ("Chore", CategoryColor.Neutral), ("Feature", CategoryColor.Green)], categories.Select(category => (category.Name, category.Color)));
	}

	[Fact]
	public async Task CreateAsync_CreatesANeutralCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		Category created = ResultAssert.Succeeded(await service.CreateAsync("Research", Token));

		Assert.Equal(CategoryColor.Neutral, created.Color);
		Assert.Equal(CategoryColor.Neutral, (await TestData.FindCategoryAsync(database, created.Id))?.Color);
	}

	[Theory]
	[InlineData(CategoryColor.Green)]
	[InlineData(CategoryColor.Pink)]
	[InlineData(CategoryColor.Neutral)]
	public async Task SetColorAsync_StoresTheColorAndReturnsTheUpdatedCategory(CategoryColor color)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);
		Category created = ResultAssert.Succeeded(await service.CreateAsync("Research", Token));
		ResultAssert.Succeeded(await service.SetColorAsync(created.Id, CategoryColor.Pink, Token));

		Category updated = ResultAssert.Succeeded(await service.SetColorAsync(created.Id, color, Token));

		Assert.Equal((created.Id, "Research", color), (updated.Id, updated.Name, updated.Color));
		Assert.Equal(color, (await TestData.FindCategoryAsync(database, created.Id))?.Color);
	}

	[Fact]
	public async Task SetColorAsync_ChangesOnlyTheNamedCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.SetColorAsync(PyxieSeedData.ChoreCategoryId, CategoryColor.Green, Token));

		IReadOnlyList<Category> categories = await service.ListAsync(Token);
		Assert.Equal([("Bug", CategoryColor.Pink), ("Chore", CategoryColor.Green), ("Feature", CategoryColor.Green)], categories.Select(category => (category.Name, category.Color)));
	}

	[Theory]
	[InlineData(3)]
	[InlineData(-1)]
	[InlineData(int.MaxValue)]
	public async Task SetColorAsync_RefusesAColorThatIsNotDefinedAndChangesNothing(int value)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.SetColorAsync(PyxieSeedData.FeatureCategoryId, (CategoryColor)value, Token));

		Assert.Equal("Category color is not recognized.", error);
		Assert.Equal(CategoryColor.Green, (await TestData.FindCategoryAsync(database, PyxieSeedData.FeatureCategoryId))?.Color);
	}

	[Fact]
	public async Task SetColorAsync_ForAMissingCategory_SaysItIsNotFound()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.SetColorAsync(999, CategoryColor.Pink, Token));

		Assert.Equal("Category not found.", error);
		Assert.Equal([CategoryColor.Pink, CategoryColor.Neutral, CategoryColor.Green], (await service.ListAsync(Token)).Select(category => category.Color));
	}
}
