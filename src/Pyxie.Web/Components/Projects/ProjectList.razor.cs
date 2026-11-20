using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Components.Projects;

/// <summary>The projects in the projects panel: each with where it came from, the open one marked and with the way to close it, the others with the way to open it, and, while an import runs, a row for the project being made. A project has one button, which says Close while the project is open and Open while it is not, so it is the same button after the change and keeps the keyboard focus. A press does what the button showed when the person pressed it, so a press the browser sent before it showed the change, or one it counts as part of a double click, does not undo it. Opening a project while another is open switches to it. Opening and closing are announced, and the page follows the announcement; a refusal is shown on the project's row. Both are refused while the pipeline runs, so the buttons wait for it and say so. A project whose folder is gone says so and cannot be opened. A project that is not open can be removed, which deletes its folder, so Remove asks first, in the row: it hands the keyboard focus to Cancel, the safe button to land on, and gets it back when the person cancels or the removal is refused. Removing is refused while the pipeline runs too, and announced like opening and closing.</summary>
public partial class ProjectList : ComponentBase, IDisposable
{
	// What the services say when they refuse, so the buttons say it before the person tries.
	private const string StopPipelineFirst = "Stop the pipeline first.";

	private const string ImportKey = "importing";

	private readonly CancellationTokenSource _cts = new();

	// Each row's Remove, by project, so the focus can go back to the one whose confirmation ended.
	private readonly Dictionary<int, Button> _removeButtons = [];

	// What the import service reports, read once per change on the component's own context, so a render never mixes two states.
	private RepositoryImport? _import;

	// The project being opened, closed, or removed, while it is.
	private int? _changing;

	// The project the last refused change was for, and the reason.
	private int? _failedId;
	private string? _error;

	// The project whose removal waits for the person to confirm it, and where the keyboard focus goes next.
	private int? _confirmingRemove;
	private Button? _cancelRemove;
	private bool _focusCancelRemove;
	private int? _focusRemoveOf;
	private bool _disposed;

	/// <summary>Gets or sets the projects, ordered as they are shown. The open one, if any, is the one whose <see cref="Project.IsOpen"/> is set.</summary>
	[Parameter]
	public required IReadOnlyList<ProjectEntry> Projects { get; set; }

	/// <summary>Gets or sets the service that opens, closes, and removes projects, supplied by the framework.</summary>
	[Inject]
	public required ProjectSessions Sessions { get; set; }

	/// <summary>Gets or sets the pipeline controller, whose running pipeline keeps the open project from changing, supplied by the framework.</summary>
	[Inject]
	public required PipelineController Controller { get; set; }

	/// <summary>Gets or sets the imports that run in the background, whose running one is shown as a project, supplied by the framework.</summary>
	[Inject]
	public required RepositoryImports Imports { get; set; }

	// One project changes at a time, so every Open, Close, and Remove waits while one does.
	private bool Busy => _changing is not null;

	// What Open, Close, and Remove wait for, which is also the title of the button's wrapper: the browser shows it there, because the wrapper still receives the pointer when the disabled button does not.
	private string? BlockedText => Controller.IsRunning ? StopPipelineFirst : null;

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Controller.StateChanged -= OnExternalChange;
		Imports.Changed -= OnExternalChange;
		_cts.Cancel();
		_cts.Dispose();
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		// Subscribe before the first read, so an import that starts or ends in between is not missed.
		Controller.StateChanged += OnExternalChange;
		Imports.Changed += OnExternalChange;
		_import = Imports.Current;
	}

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		// A project that left the list, removed here or in another tab, takes its confirmation and its button with it.
		HashSet<int> listed = [.. Projects.Select(entry => entry.Project.Id)];
		if (_confirmingRemove is int confirming && !listed.Contains(confirming))
		{
			_confirmingRemove = null;
		}

		foreach (int gone in _removeButtons.Keys.Where(id => !listed.Contains(id)).ToList())
		{
			_removeButtons.Remove(gone);
		}
	}

	/// <inheritdoc/>
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		try
		{
			if (_focusCancelRemove && _cancelRemove is not null)
			{
				_focusCancelRemove = false;
				await _cancelRemove.ElementRef.FocusAsync();
			}
			else if (_focusRemoveOf is int id)
			{
				_focusRemoveOf = null;
				if (_removeButtons.GetValueOrDefault(id) is Button remove)
				{
					await remove.ElementRef.FocusAsync();
				}
			}
		}
		catch (JSDisconnectedException)
		{
		}
	}

	private static string RowClass(ProjectEntry entry)
	{
		return entry.Project.IsOpen ? "project-row project-row-open" : "project-row";
	}

	// The importing row is named like the project it will become, and shows the origin without the credentials a person may have put in the address.
	private static string ImportOrigin(RepositoryImport import)
	{
		return GitUrl.WithoutCredentials(import.Origin);
	}

	private static string ImportName(RepositoryImport import)
	{
		return SourceNames.NameOf(ImportOrigin(import));
	}

	private static string ActionText(ProjectEntry entry)
	{
		return entry.Project.IsOpen ? "Close" : "Open";
	}

	private bool IsChanging(ProjectEntry entry)
	{
		return _changing == entry.Project.Id;
	}

	// The open project has no Remove, so a confirmation ends when its project is opened, here or in another tab.
	private bool IsConfirmingRemove(ProjectEntry entry)
	{
		return _confirmingRemove == entry.Project.Id && !entry.Project.IsOpen;
	}

	private string? ErrorOf(ProjectEntry entry)
	{
		return _failedId == entry.Project.Id ? _error : null;
	}

	// Raised on the thread that started or stopped the pipeline, or that started, failed, or finished an import: the render moves onto the component's own context.
	private void OnExternalChange()
	{
		_ = InvokeAsync(Refresh);
	}

	private void Refresh()
	{
		if (_disposed)
		{
			return;
		}

		_import = Imports.Current;
		StateHasChanged();
	}

	// The button of a project that is open closes it, and the button of one that is not opens it, as the button showed when the person pressed it. The wrapper handles the press, not the Blazorise button: that runs the Clicked it has when the press arrives, which is drawn for the project's state now, while the wrapper's handler is drawn anew with each render, so a press the browser sent before it showed a change reaches the handler drawn with what it showed. That press, a press the browser counts as part of a double click, and a press while the button is disabled, which reaches the wrapper, do nothing.
	private Task PressAsync(MouseEventArgs press, ProjectEntry entry, bool shownOpen)
	{
		bool showsNow = Projects.FirstOrDefault(listed => listed.Project.Id == entry.Project.Id)?.Project.IsOpen == shownOpen;
		if (press.Detail > 1 || Busy || BlockedText is not null || !showsNow)
		{
			return Task.CompletedTask;
		}

		// Opening or closing a project ends a removal that waits for confirmation.
		_confirmingRemove = null;

		return shownOpen ? CloseAsync(entry) : OpenAsync(entry);
	}

	// Remove only asks; nothing is deleted until the person confirms. A refusal shown on the row goes, since it was about an earlier press.
	private void BeginRemove(ProjectEntry entry)
	{
		if (Busy || BlockedText is not null)
		{
			return;
		}

		_confirmingRemove = entry.Project.Id;
		_failedId = null;
		_error = null;
		_focusCancelRemove = true;
	}

	private void CancelRemove()
	{
		_focusRemoveOf = _confirmingRemove;
		_confirmingRemove = null;
	}

	// The wrapper handles the press, as for Open and Close, so a press while the button is disabled, or one the browser counts as part of a double click, removes nothing.
	private async Task ConfirmRemoveAsync(MouseEventArgs press, ProjectEntry entry)
	{
		if (press.Detail > 1 || Busy || BlockedText is not null || _confirmingRemove != entry.Project.Id)
		{
			return;
		}

		await ChangeAsync(entry, async token =>
		{
			ServiceResult result = await Sessions.RemoveAsync(entry.Project.Id, token);

			return result.Succeeded ? null : result.Error ?? "The project could not be removed.";
		});

		_confirmingRemove = null;

		// A removal that went through takes the row away once the page reads the list again; a refused one leaves it, with the reason, and the focus goes back to its Remove.
		if (_failedId == entry.Project.Id)
		{
			_focusRemoveOf = entry.Project.Id;
		}
	}

	// Success announces the change, which replaces this list with the project, and closes any other project that was open. The service refuses when the folder went missing since the list was read, or another tab started the pipeline or is changing a project, and says why.
	private Task OpenAsync(ProjectEntry entry)
	{
		return ChangeAsync(entry, async token =>
		{
			ServiceResult<Project> result = await Sessions.OpenAsync(entry.Project.Id, token);

			return result.Succeeded ? null : result.Error ?? "The project could not be opened.";
		});
	}

	// Closing loses nothing, so it asks for no confirmation. The service closes the project this row shows and no other: it refuses, and says so, for a pipeline started since this list last rendered, and for a project that is no longer open because it was closed, or another project was opened, since.
	private Task CloseAsync(ProjectEntry entry)
	{
		return ChangeAsync(entry, async token =>
		{
			ServiceResult result = await Sessions.CloseAsync(entry.Project.Id, token);

			return result.Succeeded ? null : result.Error ?? "The project could not be closed.";
		});
	}

	// Runs an open, a close, or a removal and keeps the reason it was refused for, if it was, on the project's row.
	private async Task ChangeAsync(ProjectEntry entry, Func<CancellationToken, Task<string?>> change)
	{
		_changing = entry.Project.Id;
		_failedId = null;
		_error = null;

		try
		{
			string? refusal = await change(_cts.Token);
			if (refusal is not null)
			{
				_failedId = entry.Project.Id;
				_error = refusal;
			}
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
		finally
		{
			_changing = null;
		}
	}
}
