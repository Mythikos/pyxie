using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Repository.Navigation;

/// <summary>Turns locations into addresses of the Repository page.</summary>
public static class RepositoryNavigation
{
	/// <summary>Gets the address of a location: the current page with the location's parameters set and every unused one removed.</summary>
	/// <param name="navigation">The navigation manager.</param>
	/// <param name="location">The location.</param>
	/// <returns>The absolute address.</returns>
	public static string RepositoryUri(this NavigationManager navigation, RepositoryLocation location)
	{
		return navigation.GetUriWithQueryParameters(location.ToQuery());
	}
}
