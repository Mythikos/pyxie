using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Creates contexts on the database in a project's folder: the open project's, so every data service works on whichever project is open, or a project a service read as open (<see cref="IProjectDbContextSource"/>).</summary>
public sealed class SqliteProjectDbContextFactory : IDbContextFactory<ProjectDbContext>, IProjectDbContextSource
{
	private readonly OpenProject _openProject;

	// Built once per folder: options are immutable and the project rarely changes.
	private readonly ConcurrentDictionary<string, DbContextOptions<ProjectDbContext>> _options = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance of the <see cref="SqliteProjectDbContextFactory"/> class.</summary>
	/// <param name="openProject">The open project, whose folder holds the database.</param>
	public SqliteProjectDbContextFactory(OpenProject openProject)
	{
		_openProject = openProject;
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">No project is open.</exception>
	public ProjectDbContext CreateDbContext()
	{
		return CreateFor(_openProject.Current ?? throw new InvalidOperationException("No project is open."));
	}

	/// <inheritdoc/>
	public ProjectDbContext CreateFor(Project project)
	{
		return new ProjectDbContext(_options.GetOrAdd(project.Folder, CreateOptions));
	}

	private static DbContextOptions<ProjectDbContext> CreateOptions(string folder)
	{
		DbContextOptionsBuilder<ProjectDbContext> builder = new();
		PyxieSqlite.ConfigureProject(builder, PyxieSqlite.ConnectionString(DatabaseFiles.PathIn(folder)));

		return builder.Options;
	}
}
