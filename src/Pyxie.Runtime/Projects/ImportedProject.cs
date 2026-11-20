using Pyxie.Data.Entities;

namespace Pyxie.Runtime.Projects;

/// <summary>The machine catalog ID and connected repository created by one import.</summary>
/// <param name="ProjectId">The new project's ID.</param>
/// <param name="Repository">The repository connected to that project.</param>
public sealed record ImportedProject(int ProjectId, Repository Repository);
