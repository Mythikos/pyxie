using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Transfer;
using Pyxie.Runtime.Orchestration;
using Pyxie.Web.Components.Settings;

namespace Pyxie.Web.Components.Pages;

/// <summary>The settings of the open project, in one panel per section, with a nav that links to each panel and shows the one the URL points at.</summary>
public partial class Settings : ComponentBase, IDisposable
{
	// How many imports changed each section's settings. A panel loads its settings when it is made, so a panel whose settings an import changed is made anew, and reads them, by changing its key; the other panels keep their state, such as a Dockerfile being edited.
	private readonly Dictionary<TransferSection, int> _imports = [];
	private SettingsSection _current = SettingsSections.Agents;
	private bool _disposed;
	private Project? _project;
	private readonly HashSet<TransferSection> _pending = [];
	private AgentTable? _agents;
	private GateTable? _gates;
	private CategoryList? _categories;
	private SandboxSettingsEditor? _sandbox;

	/// <summary>Gets or sets the active project.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <summary>Gets or sets the cross-client change announcements.</summary>
	[Inject]
	public required PipelineActivity Activity { get; set; }

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
		Activity.ProjectChanged -= OnProjectChanged;
		Activity.SettingsChanged -= OnSettingsChanged;
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		_current = SettingsSections.FromUri(Navigation.Uri);
		Navigation.LocationChanged += OnLocationChanged;
		Activity.ProjectChanged += OnProjectChanged;
		Activity.SettingsChanged += OnSettingsChanged;
		_project = OpenProject.Current;
	}

	private void OnProjectChanged() => _ = InvokeAsync(() =>
	{
		if (!_disposed)
		{
			if (_project?.Id != OpenProject.Current?.Id)
			{
				_pending.Clear();
			}

			_project = OpenProject.Current;
			StateHasChanged();
		}
	});

	private void OnSettingsChanged(int projectId, TransferSection section) => _ = InvokeAsync(() =>
	{
		if (!_disposed && _project?.Id == projectId)
		{
			bool editing = section switch
			{
				TransferSection.Agents => _agents?.IsEditing == true,
				TransferSection.Gates => _gates?.IsEditing == true,
				TransferSection.Categories => _categories?.IsEditing == true,
				_ => _sandbox?.IsEditing == true,
			};
			if (editing)
			{
				_pending.Add(section);
			}
			else
			{
				Reload(section);
			}

			StateHasChanged();
		}
	});

	private object KeyOf(TransferSection section)
	{
		return (section, _imports.GetValueOrDefault(section));
	}

	private void OnImported(IReadOnlySet<TransferSection> changed)
	{
		foreach (TransferSection section in changed)
		{
			Reload(section);
		}
	}

	private void ReloadDraft(MouseEventArgs press, int projectId, TransferSection section, int shownRevision)
	{
		if (press.Detail <= 1 && projectId == _project?.Id && projectId == OpenProject.Current?.Id && _pending.Contains(section) && shownRevision == _imports.GetValueOrDefault(section))
		{
			Reload(section);
		}
	}

	private void Reload(TransferSection section)
	{
		_pending.Remove(section);
		_imports[section] = _imports.GetValueOrDefault(section) + 1;
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
