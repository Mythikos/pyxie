using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Workspaces;
using Pyxie.Web.Hosting;

namespace Pyxie.Web.Components.Settings;

/// <summary>Machine-wide access settings, available with or without an open project.</summary>
public partial class GlobalSettings : IDisposable
{
	private string? _error;
	private int _switchRevision;

	/// <summary>Gets or sets the persisted host switch.</summary>
	[Inject]
	public required McpAccessSettings Access { get; set; }

	/// <summary>Gets or sets the address of this application.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	private string Endpoint
	{
		get
		{
			UriBuilder address = new(new Uri(new Uri(Navigation.BaseUri), PyxieMcpHosting.Path.TrimStart('/')));
			if (!address.Uri.IsLoopback)
			{
				address.Host = "localhost";
			}

			return address.Uri.AbsoluteUri;
		}
	}

	/// <inheritdoc/>
	protected override void OnInitialized() => Access.Changed += OnChanged;

	/// <inheritdoc/>
	public void Dispose() => Access.Changed -= OnChanged;

	private void SetEnabled(bool enabled)
	{
		_error = Access.SetEnabled(enabled).Error;
		if (_error is not null)
		{
			// The browser toggled its checkbox before the write. Recreate it with the persisted value.
			_switchRevision++;
		}
	}

	private void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
