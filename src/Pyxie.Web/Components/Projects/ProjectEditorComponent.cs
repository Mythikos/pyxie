using Microsoft.AspNetCore.Components;

using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;

namespace Pyxie.Web.Components.Projects;

/// <summary>Binds an editor to the project it was drawn for. A project switch cannot redirect a delayed action to another project's identically numbered row.</summary>
public abstract class ProjectEditorComponent : ComponentBase
{
	private int? _projectId;

	/// <summary>Gets or sets the project shown by the enclosing page.</summary>
	[CascadingParameter(Name = nameof(DisplayedProjectId))]
	public int? DisplayedProjectId { get; set; }

	/// <summary>Gets or sets the open project for an independently rendered editor.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <summary>Gets or sets the controller that coordinates editor actions and project switches.</summary>
	[Inject]
	public required PipelineController Controller { get; set; }

	/// <inheritdoc/>
	protected override void OnInitialized() => _projectId = DisplayedProjectId ?? OpenProject.Current?.Id;

	/// <summary>Holds the displayed project until the caller finishes its action.</summary>
	/// <param name="error">The refusal suitable for display when the project is closed or changing.</param>
	/// <returns>The lease to dispose when the action ends, or null on refusal.</returns>
	protected ProjectLease? AcquireProject(out string? error)
	{
		ServiceResult<ProjectLease> result = Controller.AcquireProject(_projectId.GetValueOrDefault());
		error = result.Error;
		return result.Value;
	}
}
