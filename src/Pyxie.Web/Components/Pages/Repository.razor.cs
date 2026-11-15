using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Projects;
using Pyxie.Web.Components.Repository;
using Pyxie.Web.Components.Repository.Loading;
using Pyxie.Web.Components.Repository.Navigation;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Pages;

/// <summary>The Repository page: the projects panel, which imports, opens, and closes projects and is always there, and, while a project is open, its repository to browse. The tab, branch, folder or file, commit, and page of history live in the address, so links and back and forward work and this component keeps no navigation state of its own. It follows projects being imported, opened, or closed, here or in another tab.</summary>
public partial class Repository : ComponentBase, IDisposable
{
	private readonly LatestLoad _load = new();
	private RepositoryState _state = RepositoryState.WithoutRepository([]);
	private RepositoryLocation _requested = RepositoryLocation.FromQuery(null, null, null, null, null);
	private RepositoryLocation _location = RepositoryLocation.FromQuery(null, null, null, null, null);
	private bool _branchMissing;
	private bool _loaded;
	private bool _disposed;

	// The reads started by a change of the open project that have not finished. While one is, what the page knows describes the project that was open, so only the projects panel is shown.
	private int _reloads;

	/// <summary>Gets or sets the tab word from the address.</summary>
	[Parameter]
	[SupplyParameterFromQuery(Name = RepositoryLocation.TabKey)]
	public string? TabQuery { get; set; }

	/// <summary>Gets or sets the branch from the address.</summary>
	[Parameter]
	[SupplyParameterFromQuery(Name = RepositoryLocation.BranchKey)]
	public string? BranchQuery { get; set; }

	/// <summary>Gets or sets the folder or file from the address.</summary>
	[Parameter]
	[SupplyParameterFromQuery(Name = RepositoryLocation.PathKey)]
	public string? PathQuery { get; set; }

	/// <summary>Gets or sets the commit from the address.</summary>
	[Parameter]
	[SupplyParameterFromQuery(Name = RepositoryLocation.CommitKey)]
	public string? CommitQuery { get; set; }

	/// <summary>Gets or sets the page of history from the address.</summary>
	[Parameter]
	[SupplyParameterFromQuery(Name = RepositoryLocation.PageKey)]
	public string? PageQuery { get; set; }

	/// <summary>Gets or sets the repository browser, supplied by the framework.</summary>
	[Inject]
	public required RepositoryBrowser Browser { get; set; }

	/// <summary>Gets or sets the repository service, supplied by the framework.</summary>
	[Inject]
	public required IRepositoryService Repositories { get; set; }

	/// <summary>Gets or sets the project catalog, which lists the projects, supplied by the framework.</summary>
	[Inject]
	public required IProjectCatalog Catalog { get; set; }

	/// <summary>Gets or sets the open project, whose name the repository is shown under, supplied by the framework.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <summary>Gets or sets the announcements of changes to the repository, supplied by the framework.</summary>
	[Inject]
	public required PipelineActivity Activity { get; set; }

	private bool IsRefreshing => _reloads > 0;

	// The branch and Zip belong to the file and commit lists. One commit has no branch to choose, and a repository without branches has nothing to choose from.
	private bool ShowsBranchTools => _location.Branch.Length > 0 && _location.Tab != RepositoryTab.Branches && !(_location.Tab == RepositoryTab.History && _location.Commit.Length > 0);

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Activity.ProjectChanged -= OnProjectChanged;
		_load.Dispose();
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		// Subscribe before the first read, so a change that lands while it runs is not missed.
		Activity.ProjectChanged += OnProjectChanged;
	}

	/// <inheritdoc/>
	protected override async Task OnParametersSetAsync()
	{
		RepositoryLocation requested = RepositoryLocation.FromQuery(TabQuery, BranchQuery, PathQuery, CommitQuery, PageQuery);
		bool refresh = !_loaded || requested.Tab != _requested.Tab;
		_requested = requested;

		// The tab switches at once, with what the page already knows. Opening a tab then reads the repository again, so a branch or commit that arrived since the page opened shows up.
		ResolveLocation();

		if (refresh)
		{
			await LoadAsync();
		}
	}

	private Task LoadAsync()
	{
		return _load.RunAsync(ReadAsync, Apply);
	}

	// Raised on the thread that imported, opened, or closed a project, which can be a background import: the work moves onto the page's own context.
	private void OnProjectChanged()
	{
		_ = InvokeAsync(ReloadAfterChangeAsync);
	}

	// Owns the work started by OnProjectChanged: a failure is reported to the renderer instead of being lost with the task.
	private async Task ReloadAfterChangeAsync()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			// What the page shows of the repository no longer matches the open project, so nothing of it stays while it is read again: not the files of a project that was closed, and not those of one that was replaced. The projects panel stays, with what was typed into it.
			_reloads++;
			StateHasChanged();

			try
			{
				await LoadAsync();
			}
			finally
			{
				_reloads--;
			}

			StateHasChanged();
		}
		catch (Exception exception) when (!_disposed)
		{
			await DispatchExceptionAsync(exception);
		}
	}

	// The projects are read every time, for the panel. A repository whose hub was moved or deleted still belongs to the open project: the page keeps showing it, with git's message and the way to close the project.
	private async Task<RepositoryState> ReadAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<ProjectEntry> projects = await ReadProjectsAsync(cancellationToken);

		try
		{
			RepositoryOverview? overview = await Browser.GetOverviewAsync(cancellationToken);

			return overview is null ? RepositoryState.WithoutRepository(projects) : new RepositoryState(projects, overview.Repository, overview, null);
		}
		catch (GitException exception)
		{
			RepositoryEntity? repository = await Repositories.GetAsync(cancellationToken);

			return repository is null ? RepositoryState.WithoutRepository(projects) : new RepositoryState(projects, repository, null, $"The repository could not be read. {exception.Message}");
		}
	}

	// Whether each project's folder is still there is checked here, once per read, and not while rendering.
	private async Task<IReadOnlyList<ProjectEntry>> ReadProjectsAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<Project> projects = await Catalog.ListAsync(cancellationToken);

		return [.. projects.Select(project => new ProjectEntry(project, ProjectSessions.IsFolderPresent(project)))];
	}

	private void Apply(RepositoryState state)
	{
		_state = state;
		_loaded = true;
		ResolveLocation();
	}

	// The address may name a branch that is gone, in which case the page shows the first one and says so.
	private void ResolveLocation()
	{
		IReadOnlyList<string> names = _state.Overview is RepositoryOverview overview ? BranchSelection.Order(overview.Branches, overview.Repository.TargetBranch) : [];
		string branch = BranchSelection.Resolve(_requested.Branch, names);

		_location = _requested with { Branch = branch };
		_branchMissing = _requested.Branch.Length > 0 && _requested.Branch != branch;
	}
}
