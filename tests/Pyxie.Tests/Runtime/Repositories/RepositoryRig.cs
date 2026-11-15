using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Sqlite.DependencyInjection;
using Pyxie.Runtime.DependencyInjection;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>The project import and repository services wired as Pyxie wires them, over a workspace root of the rig's own (or one the test names) that holds the machine database and the project folders: the real <see cref="ProjectSetupService"/>, <see cref="RepositoryBrowser"/>, and project catalog, and the real git client. The pipeline is never started. Disposing it deletes the workspace root when the rig made it.</summary>
public sealed class RepositoryRig : IAsyncDisposable
{
	/// <summary>The name of the project <see cref="ConnectAsync"/> opens when none is open.</summary>
	public const string StandInProjectName = "stand-in";

	private const int MaxReadBytes = 1_000_000;

	private readonly ScratchFolder? _ownedRoot;
	private readonly ServiceProvider _provider;
	private readonly AsyncServiceScope _scope;

	/// <summary>Initializes a new instance of the <see cref="RepositoryRig"/> class and creates the machine database.</summary>
	/// <param name="workspaceRoot">The workspace root to lay out under, or <see langword="null"/> for a new temporary folder that the rig deletes.</param>
	/// <param name="repositories">A repository service to use instead of the real one, for a test that drives an import into a refusal.</param>
	/// <param name="catalog">A project catalog to use instead of the real one, for a test that holds an import at the catalog.</param>
	public RepositoryRig(string? workspaceRoot = null, IRepositoryService? repositories = null, IProjectCatalog? catalog = null)
	{
		ScratchFolder? ownedRoot = workspaceRoot is null ? ScratchFolder.Create("pyxie-ws") : null;
		string root = workspaceRoot ?? ownedRoot?.Folder ?? throw new ArgumentNullException(nameof(workspaceRoot));
		_ownedRoot = ownedRoot;

		IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Workspace:Root"] = root }).Build();
		ServiceCollection services = new();
		services.AddLogging();
		services.AddPyxieSqlite(provider => provider.GetRequiredService<WorkspaceLayout>().MachineDatabasePath);
		services.AddPyxieData();
		services.AddPyxieRuntime(configuration);
		services.AddLocalSandboxes(Path.Combine(root, "sandboxes-stand-in"));
		if (repositories is not null)
		{
			services.Replace(ServiceDescriptor.Scoped(_ => repositories));
		}

		if (catalog is not null)
		{
			services.Replace(ServiceDescriptor.Scoped(_ => catalog));
		}

		_provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		Layout = _provider.GetRequiredService<WorkspaceLayout>();
		Directory.CreateDirectory(Layout.Root);
		using (MachineDbContext machine = _provider.GetRequiredService<IDbContextFactory<MachineDbContext>>().CreateDbContext())
		{
			machine.Database.Migrate();
		}

		_scope = _provider.CreateAsyncScope();
		Git = _provider.GetRequiredService<GitClient>();
		Activity = _provider.GetRequiredService<PipelineActivity>();
		OpenProject = _provider.GetRequiredService<OpenProject>();
		Repositories = _scope.ServiceProvider.GetRequiredService<IRepositoryService>();
		Catalog = _scope.ServiceProvider.GetRequiredService<IProjectCatalog>();
		Setup = _scope.ServiceProvider.GetRequiredService<ProjectSetupService>();
		Sessions = _scope.ServiceProvider.GetRequiredService<ProjectSessions>();
		Browser = _scope.ServiceProvider.GetRequiredService<RepositoryBrowser>();
	}

	/// <summary>Gets the git client the services use.</summary>
	public GitClient Git { get; }

	/// <summary>Gets the workspace layout, whose root holds the machine database and the project folders.</summary>
	public WorkspaceLayout Layout { get; }

	/// <summary>Gets the open project.</summary>
	public OpenProject OpenProject { get; }

	/// <summary>Gets the repository service that stores the open project's repository.</summary>
	public IRepositoryService Repositories { get; }

	/// <summary>Gets the project catalog in the machine database.</summary>
	public IProjectCatalog Catalog { get; }

	/// <summary>Gets the signal the setup service raises when a project was imported.</summary>
	public PipelineActivity Activity { get; }

	/// <summary>Gets the import service under test.</summary>
	public ProjectSetupService Setup { get; }

	/// <summary>Gets the service that opens and closes projects.</summary>
	public ProjectSessions Sessions { get; }

	/// <summary>Gets the browser under test.</summary>
	public RepositoryBrowser Browser { get; }

	/// <summary>Gets the folder an import of a source names its project after: the source's last part, under the projects folder.</summary>
	/// <param name="source">The imported folder or URL.</param>
	/// <returns>The full path of the project folder the first import of that source gets.</returns>
	public string ProjectFolderFor(string source)
	{
		return Path.Combine(Layout.ProjectsFolder, SourceNames.NameOf(source));
	}

	/// <summary>Gets where the hub of the first import of a source goes.</summary>
	/// <param name="source">The imported folder or URL.</param>
	/// <returns>The full path of the hub.</returns>
	public string HubFor(string source)
	{
		return WorkspaceLayout.HubPath(ProjectFolderFor(source));
	}

	/// <summary>Gets the open project's repository.</summary>
	/// <returns>The repository, or <see langword="null"/> when no project is open or it has none.</returns>
	public async Task<Repository?> GetStoredAsync()
	{
		return OpenProject.Current is null ? null : await Repositories.GetAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Stores a repository directly, without the import's checks, so the browser can be pointed at any folder. When no project is open, an empty project named <see cref="StandInProjectName"/> is created and opened first.</summary>
	/// <param name="path">The repository folder, standing in for the hub.</param>
	/// <param name="source">Whether the repository was imported from a folder or a URL.</param>
	/// <param name="origin">The folder or URL the repository was imported from; <paramref name="path"/> unless given.</param>
	/// <param name="targetBranch">The target branch.</param>
	/// <returns>The stored repository. The test fails when the service refuses it.</returns>
	public async Task<Repository> ConnectAsync(string path, RepositorySource source = RepositorySource.Folder, string? origin = null, string targetBranch = TempGitRepository.MainBranch)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		if (OpenProject.Current is null)
		{
			string folder = Path.Combine(Layout.ProjectsFolder, StandInProjectName);
			await _provider.GetRequiredService<IProjectDatabase>().MigrateAsync(folder, token);
			Project project = ResultAssert.Succeeded(await Catalog.AddAsync(StandInProjectName, folder, origin ?? path, token));
			ResultAssert.Succeeded(await Catalog.OpenAsync(project.Id, token));
		}

		RepositoryInput input = new(source, path, origin ?? path, targetBranch);

		return ResultAssert.Succeeded(await Repositories.ConnectAsync(input, token));
	}

	/// <summary>Reads a file as it is committed at a revision of a repository, for the hub, which has no files checked out. The text is read the way the runtime reads it, so line breaks come back as LF and a final line break is dropped.</summary>
	/// <param name="repositoryPath">The repository's folder.</param>
	/// <param name="revision">A branch, tag, or commit; <c>HEAD</c> for the checked-out branch's tip.</param>
	/// <param name="relativePath">The path from the repository root, with forward slashes.</param>
	/// <returns>The file's text, or <see langword="null"/> when the path is not a text file at that revision.</returns>
	public async Task<string?> ReadFileAtAsync(string repositoryPath, string revision, string relativePath)
	{
		GitFileContent? content = await Git.ReadFileAsync(repositoryPath, revision, relativePath, MaxReadBytes, TestContext.Current.CancellationToken);

		return content?.Text;
	}

	/// <summary>Fails the test unless an import left nothing behind: no project folder, no project in the catalog, and no open project.</summary>
	/// <returns>A task that completes when the checks have run.</returns>
	public async Task AssertNothingImportedAsync()
	{
		string[] entries = Directory.Exists(Layout.ProjectsFolder) ? Directory.GetFileSystemEntries(Layout.ProjectsFolder) : [];

		Assert.True(entries.Length == 0, $"The projects folder must be empty after an import that did not finish, but holds: {string.Join(", ", entries)}");
		Assert.Empty(await Catalog.ListAsync(TestContext.Current.CancellationToken));
		Assert.Null(OpenProject.Current);
	}

	/// <summary>Releases the services and deletes the workspace root when the rig made it.</summary>
	/// <returns>A task that completes when everything the rig created is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		await _scope.DisposeAsync();
		await _provider.DisposeAsync();
		if (_ownedRoot is not null)
		{
			await _ownedRoot.DisposeAsync();
		}
	}
}
