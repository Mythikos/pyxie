using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

using Pyxie.Web.Components.Settings;

namespace Pyxie.Web.Components.Pages;

/// <summary>The settings of the open project, in one panel per section, with a nav that links to each panel and shows the one the URL points at.</summary>
public partial class Settings : ComponentBase, IDisposable
{
	private SettingsSection _current = SettingsSections.Agents;
	private bool _disposed;

	/// <summary>Gets or sets the navigation manager, supplied by the framework.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Navigation.LocationChanged -= OnLocationChanged;
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		_current = SettingsSections.FromUri(Navigation.Uri);
		Navigation.LocationChanged += OnLocationChanged;
	}

	// The app's base is the site root, so a bare fragment would link to the Board.
	private static string HrefOf(SettingsSection section)
	{
		return $"settings#{section.Id}";
	}

	// Blazor lets the browser follow a link that changes only the fragment and tells nobody, so the nav hears of a click on one of its links here. The click is not cancelled, and the link still scrolls to its panel.
	private void Select(SettingsSection section)
	{
		_current = section;
	}

	// Back and forward, and a location set by other means, are heard of through the navigation manager where Blazor reports them.
	private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
	{
		_ = InvokeAsync(() => Follow(e.Location));
	}

	private void Follow(string uri)
	{
		if (_disposed)
		{
			return;
		}

		_current = SettingsSections.FromUri(uri);
		StateHasChanged();
	}
}
