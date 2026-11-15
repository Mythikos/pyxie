using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

public sealed class UtcTimestampTests
{
	[Fact]
	public async Task ReadCard_HasUtcTimestamps()
	{
		await using TestDatabase database = new();
		WorkCard stored = await TestData.AddCardAsync(database, "Card");
		await using ProjectDbContext context = database.CreateContext();

		WorkCard card = await context.Cards.AsNoTracking().SingleAsync(existing => existing.Id == stored.Id, TestContext.Current.CancellationToken);

		Assert.Equal(DateTimeKind.Utc, card.CreatedUtc.Kind);
		Assert.Equal(DateTimeKind.Utc, card.UpdatedUtc.Kind);
	}

	[Fact]
	public async Task WriteLocalTimestamp_StoresItAsUtc()
	{
		await using TestDatabase database = new();
		DateTime local = new(2026, 3, 4, 12, 0, 0, DateTimeKind.Local);
		WorkCard stored = await TestData.AddCardAsync(database, "Card");
		await using (ProjectDbContext writer = database.CreateContext())
		{
			WorkCard card = await writer.Cards.SingleAsync(existing => existing.Id == stored.Id, TestContext.Current.CancellationToken);
			card.UpdatedUtc = local;
			await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await using ProjectDbContext reader = database.CreateContext();
		WorkCard reloaded = await reader.Cards.AsNoTracking().SingleAsync(existing => existing.Id == stored.Id, TestContext.Current.CancellationToken);

		Assert.Equal(local.ToUniversalTime(), reloaded.UpdatedUtc);
		Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedUtc.Kind);
	}

	[Fact]
	public async Task WriteLocalClosedTimestamp_StoresItAsUtcAndReadsItBackAsUtc()
	{
		await using TestDatabase database = new();
		DateTime local = new(2026, 3, 4, 12, 0, 0, DateTimeKind.Local);
		WorkCard open = await TestData.AddCardAsync(database, "Open");
		WorkCard finished = await TestData.AddCardAsync(database, "Finished", CardLane.Complete);
		await using (ProjectDbContext writer = database.CreateContext())
		{
			WorkCard card = await writer.Cards.SingleAsync(existing => existing.Id == finished.Id, TestContext.Current.CancellationToken);
			card.Lane = CardLane.Closed;
			card.ClosedUtc = local;
			await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await using ProjectDbContext reader = database.CreateContext();
		WorkCard closed = await reader.Cards.AsNoTracking().SingleAsync(existing => existing.Id == finished.Id, TestContext.Current.CancellationToken);
		WorkCard unclosed = await reader.Cards.AsNoTracking().SingleAsync(existing => existing.Id == open.Id, TestContext.Current.CancellationToken);

		Assert.NotNull(closed.ClosedUtc);
		Assert.Equal(local.ToUniversalTime(), closed.ClosedUtc.Value);
		Assert.Equal(DateTimeKind.Utc, closed.ClosedUtc.Value.Kind);
		Assert.Null(unclosed.ClosedUtc);
	}

	[Fact]
	public async Task WriteLocalConnectedTimestamp_StoresItAsUtcAndReadsItBackAsUtc()
	{
		await using TestDatabase database = new();
		DateTime local = new(2026, 3, 4, 12, 0, 0, DateTimeKind.Local);
		await using (ProjectDbContext writer = database.CreateContext())
		{
			writer.Repositories.Add(new Repository { Path = Path.Combine(Path.GetTempPath(), "pyxie-test-repository"), TargetBranch = "main", ImportedUtc = local });
			await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await using ProjectDbContext reader = database.CreateContext();
		Repository stored = await reader.Repositories.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

		Assert.Equal(local.ToUniversalTime(), stored.ImportedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.ImportedUtc.Kind);
	}
}
