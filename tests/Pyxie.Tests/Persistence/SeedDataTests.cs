using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

public sealed class SeedDataTests
{
	[Fact]
	public async Task NewDatabase_ContainsDefaultCategories()
	{
		await using TestDatabase database = new();
		await using ProjectDbContext context = database.CreateContext();

		List<Category> categories = await context.Categories.OrderBy(category => category.Id).ToListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Feature", "Bug", "Chore"], categories.Select(category => category.Name));
		Assert.Equal([1, 2, 3], categories.Select(category => category.Id));
		Assert.Equal([CategoryColor.Green, CategoryColor.Pink, CategoryColor.Neutral], categories.Select(category => category.Color));
	}

	[Fact]
	public async Task NewDatabase_ContainsNoAgentsCardsGatesOrRepository()
	{
		await using TestDatabase database = new();
		await using ProjectDbContext context = database.CreateContext();

		Assert.False(await context.Agents.AnyAsync(TestContext.Current.CancellationToken), "No agents are seeded.");
		Assert.False(await context.Cards.AnyAsync(TestContext.Current.CancellationToken), "No cards are seeded.");
		Assert.False(await context.Gates.AnyAsync(TestContext.Current.CancellationToken), "No gates are seeded.");
		Assert.False(await context.Repositories.AnyAsync(TestContext.Current.CancellationToken), "No repository is seeded.");
	}
}
