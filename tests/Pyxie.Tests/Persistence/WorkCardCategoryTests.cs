using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

public sealed class WorkCardCategoryTests
{
	[Fact]
	public async Task Model_RequiresACategoryAndRestrictsItsDeletion()
	{
		await using TestDatabase database = new();
		await using ProjectDbContext context = database.CreateContext();

		IEntityType cardType = context.Model.FindEntityType(typeof(WorkCard)) ?? throw new InvalidOperationException("WorkCard is not part of the model.");
		IForeignKey categoryKey = Assert.Single(cardType.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Category));

		Assert.False(cardType.FindProperty(nameof(WorkCard.CategoryId))?.IsNullable ?? true, "CategoryId must not be nullable.");
		Assert.True(categoryKey.IsRequired, "The category relationship must be required.");
		Assert.Equal(DeleteBehavior.Restrict, categoryKey.DeleteBehavior);
	}

	[Fact]
	public async Task Database_RefusesDeletingACategoryThatACardUses()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Card", categoryId: PyxieSeedData.BugCategoryId);
		await using ProjectDbContext context = database.CreateContext();
		Category bug = await context.Categories.SingleAsync(category => category.Id == PyxieSeedData.BugCategoryId, TestContext.Current.CancellationToken);

		context.Categories.Remove(bug);

		await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
		Assert.NotNull(await TestData.FindCategoryAsync(database, PyxieSeedData.BugCategoryId));
	}

	[Fact]
	public async Task Database_RefusesACardWhoseCategoryDoesNotExist()
	{
		await using TestDatabase database = new();
		await using ProjectDbContext context = database.CreateContext();
		DateTime now = DateTime.UtcNow;
		context.Cards.Add(new WorkCard { Name = "Card", Description = "Text", CategoryId = 999, CreatedUtc = now, UpdatedUtc = now });

		await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Database_RefusesACardWithoutACategoryValue()
	{
		await using TestDatabase database = new();
		await using ProjectDbContext context = database.CreateContext();

		await Assert.ThrowsAnyAsync<DbException>(() => context.Database.ExecuteSqlRawAsync("INSERT INTO \"Cards\" (\"Name\", \"Description\", \"Lane\", \"SortOrder\", \"State\", \"AttemptLimit\", \"Attempt\", \"Version\", \"CategoryId\", \"CreatedUtc\", \"UpdatedUtc\") VALUES ('Card', 'Text', 0, 0, 0, 3, 0, 0, NULL, '2026-01-01 00:00:00', '2026-01-01 00:00:00')", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Category_ThrowsWhenReadFromACardLoadedWithoutIt_AndIsAvailableWhenIncluded()
	{
		await using TestDatabase database = new();
		WorkCard stored = await TestData.AddCardAsync(database, "Card", categoryId: PyxieSeedData.ChoreCategoryId);
		await using ProjectDbContext context = database.CreateContext();

		WorkCard withoutCategory = await context.Cards.AsNoTracking().SingleAsync(card => card.Id == stored.Id, TestContext.Current.CancellationToken);
		WorkCard withCategory = await context.Cards.AsNoTracking().Include(card => card.Category).SingleAsync(card => card.Id == stored.Id, TestContext.Current.CancellationToken);

		Assert.Throws<InvalidOperationException>(() => withoutCategory.Category);
		Assert.Equal("Chore", withCategory.Category.Name);
		Assert.Equal(PyxieSeedData.ChoreCategoryId, withoutCategory.CategoryId);
	}
}
