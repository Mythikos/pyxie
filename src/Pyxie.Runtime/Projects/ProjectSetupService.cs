using System.Runtime.CompilerServices;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Repositories;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Projects;

/// <summary>Imports a project: a local git working copy or a URL is cloned, as a bare repository, into the hub of a new project folder under the workspace root, the project's database is created next to it, and the project is added to the catalog, opened, and given its repository. The pipeline only ever writes to the hub, so the source is never changed, and people clone the hub, pull finished work from it, and push their own commits to it; agents work in sandboxes and never reach it. The source is recorded as the project's origin, but the hub keeps no URL for it. The branch checked out in the source becomes the target branch, which must have at least one commit for card branches to start from. An import that fails or is refused leaves nothing behind.</summary>
public sealed class ProjectSetupService
{
	// Keeps project folders, and the hub and sandbox folders under them, well inside the path lengths Windows tools accept, with room for a "-N" suffix within the name limit.
	private const int MaxFolderNameLength = 64;
	private const string FallbackFolderName = "project";
	private const string ProjectOpenMessage = "A project is open. Close it to import another.";

	// Names Windows reserves for devices, with or without an extension.
	private static readonly HashSet<string> s_reservedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
	};

	private readonly IProjectCatalog _catalog;
	private readonly IProjectDatabase _projectDatabase;
	private readonly IRepositoryService _repositories;
	private readonly OpenProject _openProject;
	private readonly GitClient _git;
	private readonly WorkspaceLayout _layout;
	private readonly PipelineController _controller;
	private readonly PipelineActivity _activity;

	/// <summary>Initializes a new instance of the <see cref="ProjectSetupService"/> class.</summary>
	/// <param name="catalog">The project catalog the project is added to and opened in.</param>
	/// <param name="projectDatabase">Creates the project's database.</param>
	/// <param name="repositories">Records the project's repository.</param>
	/// <param name="openProject">The open project; an import is refused while one is open.</param>
	/// <param name="git">The git client.</param>
	/// <param name="layout">The workspace folders.</param>
	/// <param name="controller">Keeps the pipeline from starting while the project is being imported.</param>
	/// <param name="activity">Announces the imported project.</param>
	public ProjectSetupService(IProjectCatalog catalog, IProjectDatabase projectDatabase, IRepositoryService repositories, OpenProject openProject, GitClient git, WorkspaceLayout layout, PipelineController controller, PipelineActivity activity)
	{
		_catalog = catalog;
		_projectDatabase = projectDatabase;
		_repositories = repositories;
		_openProject = openProject;
		_git = git;
		_layout = layout;
		_controller = controller;
		_activity = activity;
	}

	/// <summary>Imports a local git working copy by cloning it. A folder inside the working copy imports the whole working copy; uncommitted changes are not part of the import.</summary>
	/// <param name="path">The folder.</param>
	/// <param name="cancellationToken">A token to cancel the import.</param>
	/// <returns>The imported project's repository, or a failure saying what is wrong with the folder or why the project could not be imported.</returns>
	public async Task<ServiceResult<Repository>> ImportFolderAsync(string path, CancellationToken cancellationToken) => RepositoryOf(await ImportProjectAsync(RepositorySource.Folder, path, cancellationToken));

	/// <summary>Imports a repository from a URL by cloning it.</summary>
	/// <param name="url">The URL to clone.</param>
	/// <param name="cancellationToken">A token to cancel the clone.</param>
	/// <returns>The connected repository, or a failure explaining why the project could not be imported.</returns>
	public async Task<ServiceResult<Repository>> CloneAsync(string url, CancellationToken cancellationToken) => RepositoryOf(await ImportProjectAsync(RepositorySource.Url, url, cancellationToken));

	/// <summary>Imports a repository and returns its identity from the same project change that opened it.</summary>
	/// <param name="kind">Whether to import a local worktree or clone a URL.</param>
	/// <param name="source">The worktree folder or clone URL.</param>
	/// <param name="cancellationToken">A token to cancel the import.</param>
	/// <returns>The imported project and repository, or the reason the import failed.</returns>
	public async Task<ServiceResult<ImportedProject>> ImportProjectAsync(RepositorySource kind, string source, CancellationToken cancellationToken)
	{
		if (kind == RepositorySource.Folder)
		{
			ServiceResult<string> folder = await FindWorktreeAsync(source, cancellationToken);
			if (!folder.Succeeded || folder.Value is null)
			{
				return ServiceResult<ImportedProject>.Failure(folder.Error ?? "The repository folder could not be opened.");
			}

			source = folder.Value;
		}
		else if (kind == RepositorySource.Url)
		{
			source = source.Trim();
			if (source.Length == 0)
			{
				return ServiceResult<ImportedProject>.Failure("Enter the URL to clone.");
			}
		}
		else
		{
			return ServiceResult<ImportedProject>.Failure("Unknown repository source.");
		}

		return await ImportAsync(source, kind, cancellationToken);
	}

	private static ServiceResult<Repository> RepositoryOf(ServiceResult<ImportedProject> result) =>
		result.Value is { } imported ? ServiceResult<Repository>.Success(imported.Repository) : ServiceResult<Repository>.Failure(result.Error ?? "The repository could not be imported.");

	private async Task<ServiceResult<string>> FindWorktreeAsync(string path, CancellationToken cancellationToken)
	{
		string folder = path.Trim().Trim('"');
		if (folder.Length == 0)
		{
			return ServiceResult<string>.Failure("Enter the folder of a git repository.");
		}

		if (!Path.IsPathFullyQualified(folder))
		{
			return ServiceResult<string>.Failure("Enter the folder's full path.");
		}

		if (!Directory.Exists(folder))
		{
			return ServiceResult<string>.Failure($"The folder {folder} does not exist.");
		}

		if (await _git.FindTopLevelAsync(folder, cancellationToken) is not string top)
		{
			return ServiceResult<string>.Failure($"The folder {folder} is not in a git repository.");
		}

		if (IsUnder(top, _layout.Root))
		{
			return ServiceResult<string>.Failure("That folder is inside Pyxie's own workspace folder.");
		}

		// Checked on the source, so the message names the folder the person knows rather than the hub.
		if (await TargetBranchProblemAsync(top, $"in {top}", cancellationToken) is string problem)
		{
			return ServiceResult<string>.Failure(problem);
		}

		return ServiceResult<string>.Success(top);
	}

	private static bool IsUnder(string path, string folder)
	{
		StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string prefix = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

		return full.StartsWith(prefix, comparison);
	}

	private static string LastLine(string output)
	{
		string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		return lines.Length == 0 ? "no reason given." : lines[^1];
	}

	/// <summary>Gets the name of the project folder an import of a source starts from: the source's name, made safe as a folder name on every file system. Separators and invalid characters become dashes, trailing dots and spaces go (Windows drops them), a long name is cut to keep paths short, a device name gets a suffix, and an empty name becomes <c>project</c>.</summary>
	/// <param name="source">The imported folder or URL.</param>
	/// <returns>The folder name, before any "-N" suffix for a name that is taken.</returns>
	internal static string FolderNameOf(string source)
	{
		string name = SourceNames.FileNameOf(SourceNames.NameOf(source)).Trim().TrimEnd('.');
		if (name.Length > MaxFolderNameLength)
		{
			name = name[..MaxFolderNameLength].TrimEnd('.', ' ');
		}

		if (name.Length == 0)
		{
			return FallbackFolderName;
		}

		return s_reservedNames.Contains(Path.GetFileNameWithoutExtension(name)) ? $"{name}-{FallbackFolderName}" : name;
	}

	// Only one project changes at a time, and an import does not start while a project is open, so the project it opens is the one it imported.
	private async Task<ServiceResult<ImportedProject>> ImportAsync(string source, RepositorySource kind, CancellationToken cancellationToken)
	{
		if (_openProject.Current is not null)
		{
			return ServiceResult<ImportedProject>.Failure(ProjectOpenMessage);
		}

		ServiceResult change = await _controller.BeginProjectChangeAsync(cancellationToken);
		if (!change.Succeeded)
		{
			return ServiceResult<ImportedProject>.Failure(change.Error ?? ProjectOpenMessage);
		}

		// Set once the project is in the catalog. A page can read the catalog while the import runs, so a project that was listed is announced even when the import then took it away again.
		StrongBox<bool> listed = new();
		try
		{
			return await ImportDuringChangeAsync(source, kind, listed, cancellationToken);
		}
		finally
		{
			_controller.EndProjectChange();

			if (listed.Value)
			{
				_activity.NotifyProjectChanged();
			}
		}
	}

	// Clones the hub, creates the database, and adds, opens, and connects the project; when any step fails, everything done so far is undone.
	private async Task<ServiceResult<ImportedProject>> ImportDuringChangeAsync(string source, RepositorySource kind, StrongBox<bool> listed, CancellationToken cancellationToken)
	{
		// Read again now that no other change can run: another tab may have opened a project since the first check.
		if (await _catalog.GetOpenAsync(cancellationToken) is not null)
		{
			return ServiceResult<ImportedProject>.Failure(ProjectOpenMessage);
		}

		string displaySource = GitUrl.DisplaySource(source);
		(string name, string folder) = await ChooseFolderAsync(displaySource, cancellationToken);
		string hub = WorkspaceLayout.HubPath(folder);
		Project? added = null;
		bool opened = false;
		bool connected = false;

		try
		{
			ProcessResult clone = await _git.CloneBareAsync(source, hub, cancellationToken);
			if (!clone.Succeeded)
			{
				string reason = source == displaySource
					? clone.Outcome == ProcessOutcome.Exited ? LastLine(clone.Output) : clone.StartError ?? "git timed out."
					: "Check the repository address, credentials and connectivity.";
				return ServiceResult<ImportedProject>.Failure($"git could not clone {displaySource}: {reason}");
			}

			if (await TargetBranchProblemAsync(hub, $"in {displaySource}", cancellationToken) is string problem)
			{
				return ServiceResult<ImportedProject>.Failure(problem);
			}

			string branch = await _git.GetCurrentBranchAsync(hub, cancellationToken) ?? string.Empty;
			await _git.SetUpHubAsync(hub, branch, cancellationToken);
			ServiceResult migration = await _projectDatabase.MigrateAsync(folder, cancellationToken);
			if (!migration.Succeeded)
			{
				return ServiceResult<ImportedProject>.Failure(migration.Error ?? "The project's database could not be created.");
			}

			ServiceResult<Project> add = await _catalog.AddAsync(name, folder, displaySource, cancellationToken);
			if (!add.Succeeded || add.Value is null)
			{
				return ServiceResult<ImportedProject>.Failure(add.Error ?? "The project could not be added.");
			}

			added = add.Value;
			listed.Value = true;

			// Opening closes whichever other project is open, so this must never run with one open. The check at the start of this method guarantees it: it ran inside this change, where nothing else opens or closes a project, so the only project this opens is the one just imported and it closes none.
			ServiceResult<Project> open = await _catalog.OpenAsync(added.Id, cancellationToken);
			if (!open.Succeeded)
			{
				return ServiceResult<ImportedProject>.Failure(open.Error ?? "The project could not be opened.");
			}

			opened = true;
			ServiceResult<Repository> result = await _repositories.ConnectAsync(new RepositoryInput(kind, hub, displaySource, branch), cancellationToken);
			if (!result.Succeeded || result.Value is null)
			{
				return ServiceResult<ImportedProject>.Failure(result.Error ?? "The repository could not be connected.");
			}

			connected = true;
			return ServiceResult<ImportedProject>.Success(new ImportedProject(added.Id, result.Value));
		}
		finally
		{
			if (!connected)
			{
				await UndoAsync(added, opened, folder);
			}
		}
	}

	// The folder is named after the source; a name or folder that is taken (on disk or by a registered project) gets "-2", "-3", and so on.
	private async Task<(string Name, string Folder)> ChooseFolderAsync(string source, CancellationToken cancellationToken)
	{
		IReadOnlyList<Project> projects = await _catalog.ListAsync(cancellationToken);
		string baseName = FolderNameOf(source);

		for (int number = 1; ; number++)
		{
			string name = number == 1 ? baseName : $"{baseName}-{number}";
			string folder = Path.Combine(_layout.ProjectsFolder, name);
			bool taken = Directory.Exists(folder) || File.Exists(folder) || projects.Any(project => string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(project.Folder, folder, StringComparison.OrdinalIgnoreCase));

			if (!taken)
			{
				return (name, folder);
			}
		}
	}

	// The import's token is Pyxie's shutdown token, so undoing never uses it: a cancelled import still leaves nothing behind.
	private async Task UndoAsync(Project? added, bool opened, string folder)
	{
		if (added is not null)
		{
			if (opened)
			{
				await _catalog.CloseAsync(added.Id, CancellationToken.None);
			}

			await _catalog.RemoveAsync(added.Id, CancellationToken.None);
		}

		Folders.Delete(folder);
	}

	// Card branches start from the target branch, so it must be checked out (not a detached HEAD) and have a commit.
	private async Task<string?> TargetBranchProblemAsync(string repository, string where, CancellationToken cancellationToken)
	{
		if (await _git.GetCurrentBranchAsync(repository, cancellationToken) is not string branch)
		{
			return $"No branch is checked out {where}. Check out the branch finished cards should be merged into, then import it.";
		}

		if (await _git.ResolveCommitAsync(repository, $"refs/heads/{branch}", cancellationToken) is null)
		{
			return $"The branch {branch} {where} has no commits yet. Make a first commit, then import it.";
		}

		return null;
	}
}
