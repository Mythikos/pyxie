using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Data.Sqlite.Persistence;

namespace Pyxie.Tests.Support;

/// <summary>Opens the database file in a project's folder directly, configured as Pyxie configures it, so a test can read what an operation left there without going through the open project.</summary>
public static class ProjectDatabaseFile
{
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
