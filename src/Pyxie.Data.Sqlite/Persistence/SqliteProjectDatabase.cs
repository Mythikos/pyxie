using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Creates and upgrades the SQLite database in a project's folder with this assembly's project migrations.</summary>
public sealed class SqliteProjectDatabase : IProjectDatabase
{
	/// <inheritdoc/>
	public async Task MigrateAsync(string folder, CancellationToken cancellationToken)
	{
		// SQLite creates the file but not the folders above it.
		Directory.CreateDirectory(folder);

		DbContextOptionsBuilder<ProjectDbContext> builder = new();
		PyxieSqlite.ConfigureProject(builder, PyxieSqlite.ConnectionString(DatabaseFiles.PathIn(folder)));
		await using ProjectDbContext context = new(builder.Options);
		await context.Database.MigrateAsync(cancellationToken);
	}
}
