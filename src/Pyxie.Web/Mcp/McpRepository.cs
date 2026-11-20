using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Projects;

namespace Pyxie.Web.Mcp;

/// <summary>Only repository metadata intended for display crosses the protocol boundary, including for projects imported with an older build.</summary>
internal sealed record McpRepository(int ProjectId, RepositorySource Source, string? Hub, string Origin, string TargetBranch)
{
	internal static McpRepository From(ImportedProject imported) => From(imported.ProjectId, imported.Repository);

	internal static McpRepository From(int projectId, Repository repository)
	{
		string origin = GitUrl.DisplaySource(repository.Origin);
		// Older imports named the project folder from the unredacted URL.
		string? hub = origin == repository.Origin ? repository.Path : null;
		return new(projectId, repository.Source, hub, origin, repository.TargetBranch);
	}
}
