using Pyxie.Data.Entities;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Mcp;

/// <summary>Catalog details for an external client. Older imports can have credentials in the generated name and folder.</summary>
internal sealed record McpProject(int Id, string Name, string? Folder, string Source, DateTime CreatedUtc, bool IsOpen)
{
	internal static McpProject From(Project project)
	{
		string source = GitUrl.DisplaySource(project.Source);
		// Old catalogs removed user information from Source even when the folder was named after a URL with no repository path.
		bool authorityOnly = source.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(source, UriKind.Absolute, out Uri? url) && url.AbsolutePath.Trim('/').Length == 0;
		bool hideDerivedFields = source != project.Source || authorityOnly;
		return new(project.Id, hideDerivedFields ? SourceNames.NameOf(source) : project.Name, hideDerivedFields ? null : project.Folder, source, project.CreatedUtc, project.IsOpen);
	}
}
