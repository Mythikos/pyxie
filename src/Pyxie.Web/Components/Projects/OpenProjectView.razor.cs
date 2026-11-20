using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Runtime.Orchestration;

namespace Pyxie.Web.Components.Projects;

/// <summary>Shows its content for the open project, or something else while none is open, and follows projects being imported, opened, closed, or removed, on any page and in any tab. The content is created again for each project, so nothing a child read from one project's database stays on screen once another is open.</summary>
public partial class OpenProjectView : ComponentBase, IDisposable
{
	// The project as last announced. An import announces only after it finished, so a project it opens and then rolls back never shows.
	private Project? _project;
	private bool _disposed;

	/// <summary>Gets or sets the content shown while a project is open, given that project.</summary>
	[Parameter]
	public RenderFragment<Project>? ChildContent { get; set; }

	/// <summary>Gets or sets what is shown while no project is open; nothing when it is not set.</summary>
	[Parameter]
	public RenderFragment? Closed { get; set; }

	/// <summary>Gets or sets the open project, supplied by the framework.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <summary>Gets or sets the announcements of changes to the open project, supplied by the framework.</summary>
	[Inject]
	public required PipelineActivity Activity { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Activity.ProjectChanged -= OnProjectChanged;
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		// Subscribe before the first read, so a change that lands in between is not missed.
		Activity.ProjectChanged += OnProjectChanged;
		_project = OpenProject.Current;
	}

	// Raised on the thread that imported, opened, closed, or removed a project, which can be a background import: the work moves onto the component's own context.
	private void OnProjectChanged()
	{
		_ = InvokeAsync(Refresh);
	}

	// Several changes can land before this runs, a close and then an open in another tab, so the project is read here, and the key on the content tells one project from the next.
	private void Refresh()
	{
		if (_disposed)
		{
			return;
		}

		_project = OpenProject.Current;
		StateHasChanged();
	}
}
