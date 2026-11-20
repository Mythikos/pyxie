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

		ResultAssert.Succeeded(await new SqliteProjectDatabase().MigrateAsync(folder, Token));

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
		ResultAssert.Succeeded(await projectDatabase.MigrateAsync(folder, Token));
		await using (ProjectDbContext context = ProjectDatabaseFile.Open(folder))
		{
			context.Categories.Add(new Category { Name = "Docs" });
			await context.SaveChangesAsync(Token);
		}

		ResultAssert.Succeeded(await projectDatabase.MigrateAsync(folder, Token));

		await using ProjectDbContext reopened = ProjectDatabaseFile.Open(folder);
		Assert.Equal(["Feature", "Bug", "Chore", "Docs"], await reopened.Categories.OrderBy(category => category.Id).Select(category => category.Name).ToListAsync(Token));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MigrateAsync_WithAnUnknownAppliedMigration_RefusesBeforeWritingEvenIfTheCurrentMigrationIsAlsoApplied(bool keepCurrentMigration)
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		SqliteProjectDatabase projectDatabase = new();
		ResultAssert.Succeeded(await projectDatabase.MigrateAsync(scratch.Folder, Token));
		await using (ProjectDbContext context = ProjectDatabaseFile.Open(scratch.Folder))
		{
			context.Categories.Add(new Category { Name = "Keep me" });
			await context.SaveChangesAsync(Token);
			if (keepCurrentMigration)
			{
				await context.Database.ExecuteSqlRawAsync("INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20000101000000_InitialCreate', '10.0.0')", Token);
			}
			else
			{
				await ProjectDatabaseFile.MakeUnopenableAsync(scratch.Folder, fromAnotherBuild: true, Token);
			}
		}

		byte[] before = await File.ReadAllBytesAsync(DatabaseFiles.PathIn(scratch.Folder), Token);

		string error = ResultAssert.Failed(await projectDatabase.MigrateAsync(scratch.Folder, Token));

		Assert.Contains("different build of Pyxie", error, StringComparison.Ordinal);
		Assert.Contains("Use that build to export", error, StringComparison.Ordinal);
		Assert.Contains("remove the project and import its repository again", error, StringComparison.Ordinal);
		Assert.Equal(before, await File.ReadAllBytesAsync(DatabaseFiles.PathIn(scratch.Folder), Token));
		await using ProjectDbContext reopened = ProjectDatabaseFile.Open(scratch.Folder);
		Assert.Contains(await reopened.Categories.Select(category => category.Name).ToListAsync(Token), name => name == "Keep me");
		Assert.Contains("20000101000000_InitialCreate", await reopened.Database.GetAppliedMigrationsAsync(Token));
	}

	[Fact]
	public async Task MigrateAsync_WithAnUnreadableDatabase_RefusesWithRecoveryInstructionsAndPreservesTheFile()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		await ProjectDatabaseFile.MakeUnopenableAsync(scratch.Folder, fromAnotherBuild: false, Token);
		byte[] before = await File.ReadAllBytesAsync(DatabaseFiles.PathIn(scratch.Folder), Token);

		string error = ResultAssert.Failed(await new SqliteProjectDatabase().MigrateAsync(scratch.Folder, Token));

		Assert.Contains("damaged or is not a SQLite database", error, StringComparison.Ordinal);
		Assert.Contains("Restore a backup", error, StringComparison.Ordinal);
		Assert.Contains("remove the project and import its repository again", error, StringComparison.Ordinal);
		Assert.Equal(before, await File.ReadAllBytesAsync(DatabaseFiles.PathIn(scratch.Folder), Token));
	}

	[Fact]
	public async Task MigrateAsync_WhenApplyingAMigrationFails_ReturnsTheReason()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		SqliteProjectDatabase projectDatabase = new();
		ResultAssert.Succeeded(await projectDatabase.MigrateAsync(scratch.Folder, Token));
		await using (ProjectDbContext context = ProjectDatabaseFile.Open(scratch.Folder))
		{
			await context.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\"", Token);
		}

		string error = ResultAssert.Failed(await projectDatabase.MigrateAsync(scratch.Folder, Token));

		Assert.Contains("could not be read or updated", error, StringComparison.Ordinal);
		Assert.Contains("already exists", error, StringComparison.Ordinal);
		await using ProjectDbContext reopened = ProjectDatabaseFile.Open(scratch.Folder);
		Assert.Empty(await reopened.Database.GetAppliedMigrationsAsync(Token));
		Assert.Equal(3, await reopened.Categories.CountAsync(Token));
	}

	[Fact]
	public async Task MigrateAsync_WhenTheFolderIsAFile_ReturnsAnAccessRefusal()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		string folder = scratch.Combine("app");
		await File.WriteAllTextAsync(folder, "keep me", Token);

		string error = ResultAssert.Failed(await new SqliteProjectDatabase().MigrateAsync(folder, Token));

		Assert.Contains("Check access to the project folder and try again", error, StringComparison.Ordinal);
		Assert.Equal("keep me", await File.ReadAllTextAsync(folder, Token));
	}

	[Fact]
	public async Task MigrateAsync_WhenCancelled_ThrowsCancellationWithoutCreatingTheFolder()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		string folder = scratch.Combine("cancelled");
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SqliteProjectDatabase().MigrateAsync(folder, cancellation.Token));

		Assert.False(Directory.Exists(folder), "Cancelled setup does not create a database or its folder.");
	}
}
