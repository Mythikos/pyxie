using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Data.Sqlite.Persistence;

namespace Pyxie.Tests.Support;

/// <summary>Opens the database file in a project's folder directly, configured as Pyxie configures it, so a test can read what an operation left there without going through the open project.</summary>
public static class ProjectDatabaseFile
{
	/// <summary>Makes a scratch project's database unopenable, either by recording a migration from another build or by replacing it with a non-database file.</summary>
	/// <param name="projectFolder">The scratch project's folder.</param>
	/// <param name="fromAnotherBuild">Whether to keep a valid database with an unknown migration instead of an unreadable file.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the database is unopenable.</returns>
	public static async Task MakeUnopenableAsync(string projectFolder, bool fromAnotherBuild, CancellationToken cancellationToken)
	{
		if (fromAnotherBuild)
		{
			await using ProjectDbContext context = Open(projectFolder);
			await context.Database.ExecuteSqlRawAsync("UPDATE \"__EFMigrationsHistory\" SET \"MigrationId\" = '20000101000000_InitialCreate'", cancellationToken);
		}
		else
		{
			await File.WriteAllTextAsync(DatabaseFiles.PathIn(projectFolder), "this is not a database", cancellationToken);
		}
	}

	/// <summary>Creates a context on the database in a project's folder; the caller disposes it. The connection is not pooled, so the file is released once the context is disposed.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <returns>A new context.</returns>
	public static ProjectDbContext Open(string projectFolder)
	{
		DbContextOptionsBuilder<ProjectDbContext> builder = new();
		PyxieSqlite.ConfigureProject(builder, PyxieSqlite.ConnectionString(DatabaseFiles.PathIn(projectFolder)));

		return new ProjectDbContext(builder.Options);
	}
}
