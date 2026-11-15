using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Components.Projects;

/// <summary>The projects in the projects panel: each with where it came from, the open one marked and with the way to close it, the others with the way to open it, and, while an import runs, a row for the project being made. A project has one button, which says Close while the project is open and Open while it is not, so it is the same button after the change and keeps the keyboard focus. Opening and closing are announced, and the page follows the announcement; a refusal is shown on the project's row. Both are refused while the pipeline runs, and opening while another project is open, so the buttons wait for the same reasons and say them. A project whose folder is gone says so and cannot be opened.</summary>
public partial class ProjectList : ComponentBase, IDisposable
{
	// What the services say when they refuse, so the buttons say it before the person tries.
	private const string StopPipelineFirst = "Stop the pipeline first.";
	private const string AnotherProjectOpen = "Another project is open. Close it first.";

	private const string ImportKey = "importing";

	private readonly CancellationTokenSource _cts = new();

	// What the import service reports, read once per change on the component's own context, so a render never mixes two states.
	private RepositoryImport? _import;

	// The project being opened or closed, while it is.
	private int? _changing;

	// The project the last refused change was for, and the reason.
	private int? _failedId;
	private string? _error;
	private bool _disposed;

	/// <summary>Gets or sets the projects, ordered as they are shown. The open one, if any, is the one whose <see cref="Project.IsOpen"/> is set.</summary>
	[Parameter]
	public required IReadOnlyList<ProjectEntry> Projects { get; set; }

	/// <summary>Gets or sets the service that opens and closes projects, supplied by the framework.</summary>
	[Inject]
	public required ProjectSessions Sessions { get; set; }

	/// <summary>Gets or sets the pipeline controller, whose running pipeline keeps the open project from changing, supplied by the framework.</summary>
	[Inject]
	public required PipelineController Controller { get; set; }

	/// <summary>Gets or sets the imports that run in the background, whose running one is shown as a project, supplied by the framework.</summary>
	[Inject]
	public required RepositoryImports Imports { get; set; }

	// One project changes at a time, so every Open and Close waits while one does.
	private bool Busy => _changing is not null;

	private bool PipelineRunning => Controller.IsRunning;

	private bool AnotherProjectIsOpen => Projects.Any(entry => entry.Project.IsOpen);

	// Why Open waits, in the order the service refuses: the running pipeline first, then the project that is open. The browser shows it for the wrapper, which still receives the pointer when the disabled button does not.
	private string? OpenBlockedText => PipelineRunning ? StopPipelineFirst : AnotherProjectIsOpen ? AnotherProjectOpen : null;

	private string? CloseBlockedText => PipelineRunning ? StopPipelineFirst : null;

	// What the project's button waits for, which is also the title of its wrapper.
	private string? ActionBlockedText(ProjectEntry entry)
	{
		return entry.Project.IsOpen ? CloseBlockedText : OpenBlockedText;
	}

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

	// The button of a project that is open closes it, and the button of one that is not opens it, as the button says when it is drawn.
	private Task ToggleAsync(ProjectEntry entry)
	{
		return entry.Project.IsOpen ? CloseAsync(entry) : OpenAsync(entry);
	}

	// Success announces the change, which replaces this list with the project. The service refuses when the folder went missing since the list was read, or another tab opened, imported, or is changing a project, and says why.
	private Task OpenAsync(ProjectEntry entry)
	{
		return ChangeAsync(entry, async token =>
		{
			ServiceResult<Project> result = await Sessions.OpenAsync(entry.Project.Id, token);

			return result.Succeeded ? null : result.Error ?? "The project could not be opened.";
		});
	}

	// Closing loses nothing, so it asks for no confirmation. The service refuses while the pipeline runs, and says so, for a pipeline started since this list last rendered.
	private Task CloseAsync(ProjectEntry entry)
	{
		return ChangeAsync(entry, async token =>
		{
			ServiceResult result = await Sessions.CloseAsync(token);

			return result.Succeeded ? null : result.Error ?? "The project could not be closed.";
		});
	}

	// Runs an open or a close and keeps the reason it was refused for, if it was, on the project's row.
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
