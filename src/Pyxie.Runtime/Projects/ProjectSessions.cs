using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Projects;

/// <summary>Opens and closes projects. Both are refused while the pipeline is started and wait while the stopped pipeline finishes the work it began, so the project the pipeline works on never changes under it, and both are announced. Opening a project while another is open switches to it: the catalog closes the open one as it opens the chosen one, and the switch is announced once. Closing names the project to close and closes it only while it is the open one.</summary>
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

	/// <summary>Opens a project, bringing its database up to date first. Another project that is open is closed in the same step. Everything that can refuse or fail is checked before the catalog switches, so a project that cannot be opened leaves the open project open and <see cref="OpenProject.Current"/> as it was.</summary>
	/// <param name="id">The project's identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>
	/// The open project, or a failure while the pipeline is started, when the project does not exist, or when its folder, database, or hub is gone.
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

	/// <summary>Closes a project, but only while it is the open one: a caller whose list is out of date, or another tab, cannot close a project that was opened since, because the catalog checks the project in the same write that closes it. Nothing is lost: the project stays in the list with everything in its folder. Only a close that happened is announced.</summary>
	/// <param name="id">The identifier of the project to close.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure while the pipeline is started, when the project does not exist, or when it is not open, in which case the open project stays open and nothing is announced.</returns>
	public async Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken)
	{
		ServiceResult change = await _controller.BeginProjectChangeAsync(cancellationToken);
		if (!change.Succeeded)
		{
			return change;
		}

		ServiceResult result;
		try
		{
			result = await _catalog.CloseAsync(id, cancellationToken);
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

		if (!IsFolderPresent(project))
		{
			return ServiceResult<Project>.Failure($"The folder {project.Folder} is gone.");
		}

		// The target's database is brought up to date while the open project is still open: if that fails, nothing has switched. The catalog then closes the open project and opens the target in one transaction.
		await _projectDatabase.MigrateAsync(project.Folder, cancellationToken);

		return await _catalog.OpenAsync(id, cancellationToken);
	}
}
