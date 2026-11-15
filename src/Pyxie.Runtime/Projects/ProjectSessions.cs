using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Projects;

/// <summary>Opens and closes projects. Both are refused while the pipeline is started and wait while the stopped pipeline finishes the work it began, so the project the pipeline works on never changes under it, and both are announced. Switching projects is a close followed by an open.</summary>
public sealed class ProjectSessions
{
	private readonly IProjectCatalog _catalog;
	private readonly IProjectDatabase _projectDatabase;
	private readonly PipelineController _controller;
	private readonly PipelineActivity _activity;

	/// <summary>Initializes a new instance of the <see cref="ProjectSessions"/> class.</summary>
	/// <param name="catalog">The project catalog.</param>
	/// <param name="projectDatabase">Brings a project's database up to date before it opens.</param>
	/// <param name="controller">Refuses the change while the pipeline works.</param>
	/// <param name="activity">Announces the opened or closed project.</param>
	public ProjectSessions(IProjectCatalog catalog, IProjectDatabase projectDatabase, PipelineController controller, PipelineActivity activity)
	{
		_catalog = catalog;
		_projectDatabase = projectDatabase;
		_controller = controller;
		_activity = activity;
	}

	/// <summary>Gets a value indicating whether a project's folder still holds what opening it needs: the folder itself, its database, and its hub.</summary>
	/// <param name="project">The project.</param>
	/// <returns><see langword="true"/> when the project can be opened as far as its files go.</returns>
	public static bool IsFolderPresent(Project project)
	{
		return Directory.Exists(project.Folder) && File.Exists(DatabaseFiles.PathIn(project.Folder)) && Directory.Exists(WorkspaceLayout.HubPath(project.Folder));
	}

	/// <summary>Opens a project, bringing its database up to date first.</summary>
	/// <param name="id">The project's identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>
	/// The open project, or a failure while the pipeline is started, when another project is open, when the project does not exist, or when its folder, database, or hub is gone.
	/// </returns>
	public async Task<ServiceResult<Project>> OpenAsync(int id, CancellationToken cancellationToken)
	{
		ServiceResult change = await _controller.BeginProjectChangeAsync(cancellationToken);
		if (!change.Succeeded)
		{
			return ServiceResult<Project>.Failure(change.Error ?? "The project cannot be opened now.");
		}

		ServiceResult<Project> result;
		try
		{
			result = await OpenDuringChangeAsync(id, cancellationToken);
		}
		finally
		{
			_controller.EndProjectChange();
		}

		if (result.Succeeded)
		{
			_activity.NotifyProjectChanged();
		}

		return result;
	}

	/// <summary>Closes the open project. Nothing is lost: it stays in the list with everything in its folder.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure while the pipeline is started, or when no project is open.</returns>
	public async Task<ServiceResult> CloseAsync(CancellationToken cancellationToken)
	{
		ServiceResult change = await _controller.BeginProjectChangeAsync(cancellationToken);
		if (!change.Succeeded)
		{
			return change;
		}

		ServiceResult result;
		try
		{
			result = await _catalog.CloseAsync(cancellationToken);
		}
		finally
		{
			_controller.EndProjectChange();
		}

		if (result.Succeeded)
		{
			_activity.NotifyProjectChanged();
		}

		return result;
	}

	private async Task<ServiceResult<Project>> OpenDuringChangeAsync(int id, CancellationToken cancellationToken)
	{
		IReadOnlyList<Project> projects = await _catalog.ListAsync(cancellationToken);
		if (projects.FirstOrDefault(project => project.Id == id) is not Project project)
		{
			return ServiceResult<Project>.Failure("Project not found.");
		}

		if (projects.Any(other => other.IsOpen && other.Id != id))
		{
			return ServiceResult<Project>.Failure("Another project is open. Close it first.");
		}

		if (!IsFolderPresent(project))
		{
			return ServiceResult<Project>.Failure($"The folder {project.Folder} is gone.");
		}

		await _projectDatabase.MigrateAsync(project.Folder, cancellationToken);

		return await _catalog.OpenAsync(id, cancellationToken);
	}
}
