using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Projects;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Repository;

/// <summary>
/// What the Repository page knows: the projects there are, and, for the open project, its repository, what was read from it, and why it could not be read.
/// </summary>
/// <param name="Projects">The projects, ordered as they are listed. The open one, if any, is the one whose <see cref="Pyxie.Data.Entities.Project.IsOpen"/> is set.</param>
/// <param name="Repository">The open project's repository, or <see langword="null"/> when no project is open or it has none.</param>
/// <param name="Overview">What was read from the repository, or <see langword="null"/> when there is no repository or it could not be read.</param>
/// <param name="Error">Why the repository could not be read, for example because its folder was moved; otherwise <see langword="null"/>.</param>
public sealed record RepositoryState(IReadOnlyList<ProjectEntry> Projects, RepositoryEntity? Repository, RepositoryOverview? Overview, string? Error)
{
	/// <summary>Gets a value indicating whether one of the projects is open.</summary>
	public bool HasOpenProject => Projects.Any(entry => entry.Project.IsOpen);

	/// <summary>Creates the state of the page while there is no repository to browse: no project is open, or the open one has none.</summary>
	/// <param name="projects">The projects there are.</param>
	/// <returns>The state.</returns>
	public static RepositoryState WithoutRepository(IReadOnlyList<ProjectEntry> projects)
	{
		return new RepositoryState(projects, null, null, null);
	}
}
