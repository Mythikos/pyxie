using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Pyxie.Data.Persistence;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Creates a project context for the EF Core command-line tools; it is not used at run time.</summary>
public sealed class ProjectDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
	private const string DesignTimeConnectionString = "Data Source=pyxie.project.design.db";

	/// <inheritdoc/>
	public ProjectDbContext CreateDbContext(string[] args)
	{
		DbContextOptionsBuilder<ProjectDbContext> builder = new();
		PyxieSqlite.ConfigureProject(builder, DesignTimeConnectionString);

		return new ProjectDbContext(builder.Options);
	}
}
