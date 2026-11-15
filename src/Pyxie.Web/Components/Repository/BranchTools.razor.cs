using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Repository.Navigation;
using Pyxie.Web.Hosting;

namespace Pyxie.Web.Components.Repository;

/// <summary>What the Files and History tabs share, in the bar beside the tabs: the branch they show, and the files of that branch as a zip download.</summary>
public partial class BranchTools : ComponentBase
{
	/// <summary>Gets or sets the repository whose branches are offered.</summary>
	[Parameter]
	public required RepositoryOverview Overview { get; set; }

	/// <summary>Gets or sets the current location, whose branch is the one shown and the one downloaded. Its branch is one that exists.</summary>
	[Parameter]
	public required RepositoryLocation Location { get; set; }

	// The zip is always the files of the branch on screen. The address is relative to the page's base, like the endpoint that serves it.
	private string ArchiveHref => $"{PyxieEndpointRouteBuilderExtensions.RepositoryArchivePath}?branch={Uri.EscapeDataString(Location.Branch)}";
}
