using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

/// <summary>Import, open, and startup bring a project's database up to date in its folder: a new folder gets a seeded database, and an existing database keeps what it holds.</summary>
public sealed class SqliteProjectDatabaseTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task MigrateAsync_InAFolderThatDoesNotExist_CreatesItWithAnUpToDateDatabaseHoldingTheDefaultCategories()
	{
		await using ScratchFolder projects = ScratchFolder.Create("pyxie-projects");
		string folder = projects.Combine("nested", "app");

		await new SqliteProjectDatabase().MigrateAsync(folder, Token);

		Assert.True(File.Exists(Path.Combine(folder, "pyxie.db")), "The project's database is pyxie.db in its folder.");
		await using ProjectDbContext context = ProjectDatabaseFile.Open(folder);
		Assert.Empty(await context.Database.GetPendingMigrationsAsync(Token));
		Assert.Equal(["Feature", "Bug", "Chore"], await context.Categories.OrderBy(category => category.Id).Select(category => category.Name).ToListAsync(Token));
	}

	[Fact]
	public async Task MigrateAsync_OnAnExistingDatabase_KeepsWhatItHolds()
	{
		await using ScratchFolder projects = ScratchFolder.Create("pyxie-projects");
		string folder = projects.Combine("app");
		SqliteProjectDatabase projectDatabase = new();
		await projectDatabase.MigrateAsync(folder, Token);
		await using (ProjectDbContext context = ProjectDatabaseFile.Open(folder))
		{
			context.Categories.Add(new Category { Name = "Docs" });
			await context.SaveChangesAsync(Token);
		}

		await projectDatabase.MigrateAsync(folder, Token);

		await using ProjectDbContext reopened = ProjectDatabaseFile.Open(folder);
		Assert.Equal(["Feature", "Bug", "Chore", "Docs"], await reopened.Categories.OrderBy(category => category.Id).Select(category => category.Name).ToListAsync(Token));
	}
}
