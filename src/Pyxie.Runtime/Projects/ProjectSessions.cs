using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Media;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Projects;

/// <summary>Opens, closes, and removes projects. All three are refused while the pipeline is started, or while another project is being imported, opened, closed, or removed, and wait while the stopped pipeline finishes the work it began, so the project the pipeline works on never changes under it, and all three are announced. Opening a project while another is open switches to it: the catalog closes the open one as it opens the chosen one, and the switch is announced once. Opening the project that is already open changes nothing and announces nothing, so a second press on Open that a person made before the page showed the project open does not open it again. Closing names the project to close and closes it only while it is the open one.</summary>
public sealed class ProjectSessions
{
	private readonly IProjectCatalog _catalog;
	private readonly IProjectDatabase _projectDatabase;
	private readonly PipelineController _controller;
	private readonly PipelineActivity _activity;
	private readonly ISandboxes _sandboxes;
	private readonly SandboxImageBuilds _imageBuilds;
	private readonly WorkspaceLayout _layout;
	private readonly MediaStorage _media;

	/// <summary>Initializes a new instance of the <see cref="ProjectSessions"/> class.</summary>
	/// <param name="catalog">The project catalog.</param>
	/// <param name="projectDatabase">Brings a project's database up to date before it opens.</param>
	/// <param name="controller">Refuses the change while the pipeline works.</param>
	/// <param name="activity">Announces the opened, closed, or removed project.</param>
	/// <param name="sandboxes">Removes what a removed project's sandboxes and image builds left.</param>
	/// <param name="imageBuilds">Stops a person's build of a removed project's image and forgets its builds.</param>
	/// <param name="layout">Where project folders are, the only place a removed project's folder is deleted from.</param>
	/// <param name="media">Drains cancelled uploads before deleting their project folder.</param>
	public ProjectSessions(IProjectCatalog catalog, IProjectDatabase projectDatabase, PipelineController controller, PipelineActivity activity, ISandboxes sandboxes, SandboxImageBuilds imageBuilds, WorkspaceLayout layout, MediaStorage media)
	{
		_catalog = catalog;
		_projectDatabase = projectDatabase;
		_controller = controller;
		_activity = activity;
		_sandboxes = sandboxes;
		_imageBuilds = imageBuilds;
		_layout = layout;
		_media = media;
	}

	/// <summary>Gets a value indicating whether a project's folder still holds what opening it needs: the folder itself, its database, and its hub.</summary>
	/// <param name="project">The project.</param>
	/// <returns><see langword="true"/> when the project can be opened as far as its files go.</returns>
	public static bool IsFolderPresent(Project project)
	{
		return Directory.Exists(project.Folder) && File.Exists(DatabaseFiles.PathIn(project.Folder)) && Directory.Exists(WorkspaceLayout.HubPath(project.Folder));
	}

	/// <summary>Opens a project, bringing its database up to date first. Another project that is open is closed in the same step. Everything that can refuse or fail is checked before the catalog switches, so a project that cannot be opened leaves the open project open and <see cref="OpenProject.Current"/> as it was. Opening the project that is already open succeeds without opening it again or announcing anything.</summary>
	/// <param name="id">The project's identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>
	/// The open project, or a failure while the pipeline is started, when the project does not exist, when its folder, database, or hub is gone, or when its database cannot be brought up to date. The project that is already open is returned as it is.
	/// </returns>
	public async Task<ServiceResult<Project>> OpenAsync(int id, CancellationToken cancellationToken)
	{
		ServiceResult change = await _controller.BeginProjectChangeAsync(cancellationToken);
		if (!change.Succeeded)
		{
			return ServiceResult<Project>.Failure(change.Error ?? "The project cannot be opened now.");
		}

		ServiceResult<Project> result;
		bool opened;
		try
		{
			(result, opened) = await OpenDuringChangeAsync(id, cancellationToken);
		}
		finally
		{
			_controller.EndProjectChange();
		}

		if (opened)
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

	/// <summary>Removes a closed project for good: a person's build of its image is stopped, its sandboxes (containers, volumes, and images) are removed, then its folder with everything in it (the database with its cards, history, and settings, the hub, and the round logs), and last its entry in the list. A project imported later into the same folder therefore finds nothing of this one. Each step is done before the next begins, so a removal that stops part-way leaves the project in the list, and removing it again finishes the job. The source it was imported from is never touched.</summary>
	/// <param name="id">The identifier of the project to remove.</param>
	/// <param name="cancellationToken">A token to cancel the operation. Once the folder is being deleted the removal is finished regardless.</param>
	/// <returns>A failure while the pipeline is started, when the project does not exist, is open, or has its folder outside the projects folder, when the container engine does not answer, or when the folder could not be deleted. A removal that changed something is announced, also when it then failed, so the list shows what is left.</returns>
	public async Task<ServiceResult> RemoveAsync(int id, CancellationToken cancellationToken)
	{
		ServiceResult change = await _controller.BeginProjectChangeAsync(cancellationToken);
		if (!change.Succeeded)
		{
			return change;
		}

		ServiceResult result;
		bool changed;
		try
		{
			(result, changed) = await RemoveDuringChangeAsync(id, cancellationToken);
		}
		finally
		{
			_controller.EndProjectChange();
		}

		if (changed)
		{
			_activity.NotifyProjectChanged();
		}

		return result;
	}

	// Whether the project was opened, which is announced: one that was already open is left as it is.
	private async Task<(ServiceResult<Project> Result, bool Opened)> OpenDuringChangeAsync(int id, CancellationToken cancellationToken)
	{
		IReadOnlyList<Project> projects = await _catalog.ListAsync(cancellationToken);
		if (projects.FirstOrDefault(project => project.Id == id) is not Project project)
		{
			return (ServiceResult<Project>.Failure("Project not found."), false);
		}

		if (project.IsOpen)
		{
			return (ServiceResult<Project>.Success(project), false);
		}

		if (!IsFolderPresent(project))
		{
			return (ServiceResult<Project>.Failure($"The folder {project.Folder} is gone."), false);
		}

		// The target's database is brought up to date while the open project is still open: if that fails, nothing has switched. The catalog then closes the open project and opens the target in one transaction.
		ServiceResult migration = await _projectDatabase.MigrateAsync(project.Folder, cancellationToken);
		if (!migration.Succeeded)
		{
			return (ServiceResult<Project>.Failure(migration.Error ?? "The project's database could not be brought up to date."), false);
		}

		ServiceResult<Project> result = await _catalog.OpenAsync(id, cancellationToken);

		return (result, result.Succeeded);
	}

	// Whether anything of the project was deleted, which is announced: a refusal before the folder is touched changes nothing the list shows.
	private async Task<(ServiceResult Result, bool Changed)> RemoveDuringChangeAsync(int id, CancellationToken cancellationToken)
	{
		IReadOnlyList<Project> projects = await _catalog.ListAsync(cancellationToken);
		if (projects.FirstOrDefault(project => project.Id == id) is not Project project)
		{
			return (ServiceResult.Failure("Project not found."), false);
		}

		if (project.IsOpen)
		{
			return (ServiceResult.Failure("Close the project first."), false);
		}

		// The folder is deleted with everything in it, so it must be one Pyxie made for a project.
		if (!IsProjectFolder(project.Folder))
		{
			return (ServiceResult.Failure($"The folder {project.Folder} is not in Pyxie's projects folder, so Pyxie does not delete it."), false);
		}

		await _media.DrainUploadsAsync(project.Folder, cancellationToken);
		using IDisposable mediaWrite = await _media.AcquireAsync(cancellationToken);
		string projectKey = SandboxNames.ProjectKey(project.Folder);
		await _imageBuilds.ForgetProjectAsync(projectKey);
		try
		{
			await _sandboxes.RemoveProjectAsync(projectKey, cancellationToken);
		}
		catch (SandboxUnavailableException)
		{
			return (ServiceResult.Failure("Docker is not running, so the project's sandboxes cannot be removed. Start Docker Desktop, then remove the project again."), false);
		}
		catch (SandboxException exception)
		{
			return (ServiceResult.Failure($"The project's sandboxes could not be removed: {exception.Message}"), false);
		}

		try
		{
			Folders.Delete(project.Folder);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return (ServiceResult.Failure($"The folder {project.Folder} could not be deleted: {exception.Message} Close whatever has a file in it open, then remove the project again."), true);
		}

		// The folder is gone, so the entry goes too, even when the person left the page meanwhile.
		return (await _catalog.RemoveAsync(id, CancellationToken.None), true);
	}

	private bool IsProjectFolder(string folder)
	{
		StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

		return string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layout.ProjectsFolder)), comparison);
	}
}
