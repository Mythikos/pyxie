using Microsoft.AspNetCore.Components;

using Pyxie.Web.Components.Repository.Navigation;

namespace Pyxie.Web.Components.Repository;

/// <summary>The tabs of the Repository page: Files, History, and Branches with how many there are. Each is a link to the address that shows it, so the keyboard, a middle click, and back and forward work as they do for every other link on the page, and the page keeps no tab state of its own. The tab that is shown links to where the page is, so choosing it again changes nothing; the others start at the top of their own view.</summary>
public partial class RepositoryTabs : ComponentBase
{
	/// <summary>Gets or sets the current location, whose tab is the selected one. A tab keeps its branch.</summary>
	[Parameter]
	public required RepositoryLocation Location { get; set; }

	/// <summary>Gets or sets how many branches there are, which the Branches tab shows.</summary>
	[Parameter]
	public int BranchCount { get; set; }

	/// <summary>Gets or sets the navigation manager, supplied by the framework.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	private string ClassOf(RepositoryTab tab)
	{
		return tab == Location.Tab ? "repository-tab repository-tab-selected" : "repository-tab";
	}

	// ARIA wants the words, not a flag that Blazor would leave out when it is false.
	private string SelectedText(RepositoryTab tab)
	{
		return tab == Location.Tab ? "true" : "false";
	}

	private string Href(RepositoryTab tab)
	{
		return Navigation.RepositoryUri(tab == Location.Tab ? Location : Location.ForTab(tab));
	}
}
