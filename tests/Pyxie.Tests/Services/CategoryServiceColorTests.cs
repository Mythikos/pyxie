using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A category has a color for its chip, any color at all: the default categories come in the design's tints, and a category is created in the color it is given and can be given another.</summary>
public sealed class CategoryServiceColorTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ListAsync_ReturnsTheSeededCategoriesInTheirColors()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		IReadOnlyList<Category> categories = await service.ListAsync(Token);

		Assert.Equal([("Bug", CategoryColors.Pink), ("Chore", CategoryColors.Neutral), ("Feature", CategoryColors.Green)], categories.Select(category => (category.Name, category.Color)));
	}

	[Theory]
	[InlineData("#EDEDE9")]
	[InlineData("#FBE9EC")]
	[InlineData("#1F3A93")]
	[InlineData("#FFFFFF")]
	[InlineData("#000000")]
	public async Task CreateAsync_CreatesTheCategoryInTheColorItWasGiven(string written)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);
		HexColor color = HexColor.Parse(written);

		Category created = ResultAssert.Succeeded(await service.CreateAsync("Research", color, Token));

		Assert.Equal(("Research", color), (created.Name, created.Color));
		Assert.Equal(color, (await TestData.FindCategoryAsync(database, created.Id))?.Color);
	}

	[Fact]
	public async Task CreateAsync_StoresTheColorWrittenAsUpperCaseHex()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		Category created = ResultAssert.Succeeded(await service.CreateAsync("Research", HexColor.Parse("#0a1b2c"), Token));

		Assert.Equal("#0A1B2C", await StoredTextAsync(database, created.Id));
	}

	[Fact]
	public async Task CreateAsync_RefusedForItsName_CreatesNothingInAnyColor()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.CreateAsync("bug", HexColor.Parse("#1F3A93"), Token));

		Assert.Equal([CategoryColors.Pink, CategoryColors.Neutral, CategoryColors.Green], (await service.ListAsync(Token)).Select(category => category.Color));
	}

	[Theory]
	[InlineData("#EAF3E2")]
	[InlineData("#FBE9EC")]
	[InlineData("#EDEDE9")]
	[InlineData("#1F3A93")]
	[InlineData("#000000")]
	public async Task SetColorAsync_StoresTheColorAndReturnsTheUpdatedCategory(string written)
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);
		HexColor color = HexColor.Parse(written);
		Category created = ResultAssert.Succeeded(await service.CreateAsync("Research", CategoryColors.Pink, Token));

		Category updated = ResultAssert.Succeeded(await service.SetColorAsync(created.Id, color, Token));

		Assert.Equal((created.Id, "Research", color), (updated.Id, updated.Name, updated.Color));
		Assert.Equal(color, (await TestData.FindCategoryAsync(database, created.Id))?.Color);
		Assert.Equal(color.ToString(), await StoredTextAsync(database, created.Id));
	}

	[Fact]
	public async Task SetColorAsync_ToTheColorTheCategoryHas_Succeeds()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		Category updated = ResultAssert.Succeeded(await service.SetColorAsync(PyxieSeedData.BugCategoryId, CategoryColors.Pink, Token));

		Assert.Equal(CategoryColors.Pink, updated.Color);
	}

	[Fact]
	public async Task SetColorAsync_ChangesOnlyTheNamedCategory()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.SetColorAsync(PyxieSeedData.ChoreCategoryId, CategoryColors.Green, Token));

		IReadOnlyList<Category> categories = await service.ListAsync(Token);
		Assert.Equal([("Bug", CategoryColors.Pink), ("Chore", CategoryColors.Green), ("Feature", CategoryColors.Green)], categories.Select(category => (category.Name, category.Color)));
	}

	[Fact]
	public async Task SetColorAsync_ForAMissingCategory_SaysItIsNotFound()
	{
		await using TestDatabase database = new();
		CategoryService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.SetColorAsync(999, CategoryColors.Pink, Token));

		Assert.Equal("Category not found.", error);
		Assert.Equal([CategoryColors.Pink, CategoryColors.Neutral, CategoryColors.Green], (await service.ListAsync(Token)).Select(category => category.Color));
	}

	private static async Task<string> StoredTextAsync(TestDatabase database, int id)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Database.SqlQuery<string>($"SELECT \"Color\" AS \"Value\" FROM \"Categories\" WHERE \"Id\" = {id}").SingleAsync(Token);
	}
}
