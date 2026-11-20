using Pyxie.Data.Entities;

namespace Pyxie.Data.Persistence;

/// <summary>Creates contexts on a given project's database. A service that checks which project is open before it works reads <see cref="Projects.OpenProject.Current"/> once and creates its context for that same project here, so a project opened or closed in between can neither make it work on another project nor fail it with an exception; <c>IDbContextFactory&lt;ProjectDbContext&gt;</c> reads the open project again when it creates a context.</summary>
public interface IProjectDbContextSource
{
	/// <summary>Creates a context on a project's database; the caller disposes it.</summary>
	/// <param name="project">The project, as the caller read it.</param>
	/// <returns>A new context on the project's database.</returns>
	ProjectDbContext CreateFor(Project project);
}
