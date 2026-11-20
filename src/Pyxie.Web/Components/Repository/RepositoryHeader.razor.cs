using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Git;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Repository;

/// <summary>The open project's repository at a glance: its name, target branch, and copyable origin and hub path. Closing the project is on its row in the projects panel.</summary>
public partial class RepositoryHeader : ComponentBase
{
	/// <summary>Gets or sets the open project's name, which is what the page is about.</summary>
	[Parameter]
	public required string ProjectName { get; set; }

	/// <summary>Gets or sets the open project's repository.</summary>
	[Parameter]
	public required RepositoryEntity Repository { get; set; }

	// Where the project was imported from, as recorded at import: the hub itself keeps no way back to it.
	private string OriginText => GitUrl.WithoutCredentials(Repository.Origin);
}
