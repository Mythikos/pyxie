using Pyxie.Data.Entities;

namespace Pyxie.Web.Components.Projects;

/// <summary>A project in the projects panel, with whether its folder still holds what opening it needs, checked once when the list was read.</summary>
/// <param name="Project">The project.</param>
/// <param name="IsFolderPresent">Whether the project's folder, its database, and its hub are there, so it can be opened.</param>
public sealed record ProjectEntry(Project Project, bool IsFolderPresent);
