using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Repository.Navigation;

namespace Pyxie.Web.Components.Repository;

/// <summary>Chooses the branch the Files and History tabs show, under a visible label. Choosing one navigates, so the address names the branch.</summary>
public partial class BranchPicker : ComponentBase
{
	// The label names the select's text field, which is where assistive technology looks for a name.
	private const string LabelId = "repository-branch-label";

	/// <summary>Gets or sets the repository whose branches are offered.</summary>
	[Parameter]
	public required RepositoryOverview Overview { get; set; }

	/// <summary>Gets or sets the current location, whose branch is the selected one.</summary>
	[Parameter]
	public required RepositoryLocation Location { get; set; }

	/// <summary>Gets or sets the navigation manager, supplied by the framework.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	private IReadOnlyList<string> Names => BranchSelection.Order(Overview.Branches, Overview.Repository.TargetBranch);

	private Task OnChanged(string branch)
	{
		Navigation.NavigateTo(Navigation.RepositoryUri(Location.OnBranch(branch)));

		return Task.CompletedTask;
	}
}
