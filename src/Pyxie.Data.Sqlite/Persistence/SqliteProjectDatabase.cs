using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Creates and upgrades the SQLite database in a project's folder with this assembly's project migrations.</summary>
public sealed class SqliteProjectDatabase : IProjectDatabase
{
	private const int SqliteCorrupt = 11;
	private const int SqliteNotADatabase = 26;

	/// <inheritdoc/>
	public async Task<ServiceResult> MigrateAsync(string folder, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			// SQLite creates the file but not the folders above it.
			Directory.CreateDirectory(folder);

			DbContextOptionsBuilder<ProjectDbContext> builder = new();
			PyxieSqlite.ConfigureProject(builder, PyxieSqlite.ConnectionString(DatabaseFiles.PathIn(folder)));
			await using ProjectDbContext context = new(builder.Options);
			IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
			// A regenerated InitialCreate must not run on a database made by another build, even when all of this build's migrations are already recorded too.
			if (applied.Except(context.Database.GetMigrations(), StringComparer.Ordinal).Any())
			{
				return ServiceResult.Failure("This project's database was created by a different build of Pyxie. Use that build to export its settings, or remove the project and import its repository again.");
			}

			await context.Database.MigrateAsync(cancellationToken);

			return ServiceResult.Success();
		}
		catch (SqliteException exception) when (exception.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
		{
			return ServiceResult.Failure("The project's database is damaged or is not a SQLite database. Restore a backup, or remove the project and import its repository again.");
		}
		catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
		{
			return ServiceResult.Failure($"The project's database could not be read or updated: {exception.Message} Check access to the project folder and try again.");
		}
	}
}
