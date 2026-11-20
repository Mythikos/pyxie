using Microsoft.AspNetCore.Components;

using Pyxie.Data.Enums;
using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Components.Repository;

/// <summary>The form in the projects panel that makes a new project by importing a repository, cloned from a local folder or a URL. It does not run the import: <see cref="RepositoryImports"/> does, so an import outlives the page. The form shows the import that is running, or why the last one failed, whether this form started it or an earlier visit or another tab did, and starts a new one on request. A project is imported only while none is open, so the form waits while one is.</summary>
public partial class RepositoryConnect : ComponentBase, IDisposable
{
	private const string InputId = "import-source-entry";

	// What the import service says when a project is open, so the form says it before the person tries.
	private const string ProjectOpenText = "A project is open. Close it to import another.";

	private RepositorySource _mode = RepositorySource.Folder;
	private string _folder = string.Empty;
	private string _url = string.Empty;

	// What the import service reports, read once per change on the component's own context, so a render never mixes two states.
	private RepositoryImport? _import;
	private bool _disposed;

	/// <summary>Gets or sets a value indicating whether a project is open, which keeps another from being imported.</summary>
	[Parameter]
	public bool ProjectOpen { get; set; }

	/// <summary>Gets or sets the imports that run in the background, supplied by the framework.</summary>
	[Inject]
	public required RepositoryImports Imports { get; set; }

	// An import runs for as long as git takes, so the whole form waits for it instead of letting a second one start beside it.
	private bool Running => _import is { IsRunning: true };

	private bool Blocked => Running || ProjectOpen;

	// The reason the disabled form gives when hovered; the browser shows it for the wrapper, which still receives the pointer when the controls do not. A running import is already shown by the button.
	private string? BlockedText => ProjectOpen && !Running ? ProjectOpenText : null;

	private string ModeLabel => _mode == RepositorySource.Folder ? "Folder" : "URL";

	private string Placeholder => _mode == RepositorySource.Folder ? @"D:\src\my-app" : "https://example.com/app.git";

	// One field serves both ways of importing, and each way keeps what was entered for it.
	private string Entry => _mode == RepositorySource.Folder ? _folder : _url;

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Imports.Changed -= OnImportsChanged;
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		// Subscribe before the first read, so an import that ends in between is not missed.
		Imports.Changed += OnImportsChanged;
		Follow();
	}

	// The reason a failed import gave, shown with the way of importing that was tried.
	private string? ErrorOf(RepositorySource source)
	{
		return _import is { } failed && failed.Source == source ? failed.Error : null;
	}

	// Takes the import the service reports. What was entered for a running or failed import goes back into the field, so a form opened later, or in another tab, shows what is being imported.
	private void Follow()
	{
		RepositoryImport? before = _import;
		_import = Imports.Current;

		if (_import is null)
		{
			// An import this form saw run that left no failure succeeded: what was entered is now a project, and the form is ready for the next one.
			if (before is { IsRunning: true })
			{
				_folder = string.Empty;
				_url = string.Empty;
			}

			return;
		}

		_mode = _import.Source;

		if (_import.Source == RepositorySource.Folder)
		{
			_folder = _import.Origin;
		}
		else
		{
			_url = _import.Origin;
		}
	}

	// Starting replaces the error of the last failed import. The form follows the import from here, whatever page the person is on. A form that shows itself disabled starts nothing: the disabled controls only stop the browser, and a submit it sent before it showed the form busy, or a project open, still arrives.
	private void Submit()
	{
		if (Blocked)
		{
			return;
		}

		Imports.TryStart(_mode, Entry);
		Follow();
	}

	private void OnModeChanged(RepositorySource mode)
	{
		_mode = mode;
	}

	private void OnEntryChanged(string value)
	{
		if (_mode == RepositorySource.Folder)
		{
			_folder = value;
		}
		else
		{
			_url = value;
		}

		Edit(_mode);
	}

	// An error describes what was entered, so editing its field takes the error away.
	private void Edit(RepositorySource source)
	{
		if (_import is { IsRunning: false } failed && failed.Source == source)
		{
			Imports.Dismiss();
		}
	}

	// Raised on whichever thread started, failed, or finished an import: the work moves onto the component's own context.
	private void OnImportsChanged()
	{
		_ = InvokeAsync(RefreshAsync);
	}

	// Owns the work started by OnImportsChanged: a failure is reported to the renderer instead of being lost with the task.
	private async Task RefreshAsync()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			Follow();
			StateHasChanged();
		}
		catch (Exception exception) when (!_disposed)
		{
			await DispatchExceptionAsync(exception);
		}
	}
}
