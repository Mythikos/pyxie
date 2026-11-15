using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>An import creates a project: a folder under the projects folder, named after the source, that holds the project's own database and its hub, registered in the machine database and open. A name that is taken, on disk or by a registered project, gets the next free number.</summary>
public sealed class ProjectSetupServiceProjectFolderTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(RepositorySource.Folder)]
	[InlineData(RepositorySource.Url)]
	public async Task Import_CreatesAnOpenProjectWhoseFolderHoldsItsSeededDatabaseWithTheRepositoryAndItsBareHub(RepositorySource source)
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		DateTime before = DateTime.UtcNow;

		Repository connected = ResultAssert.Succeeded(source == RepositorySource.Folder ? await rig.Setup.ImportFolderAsync(origin.Folder, Token) : await rig.Setup.CloneAsync(origin.Folder, Token));

		DateTime after = DateTime.UtcNow;
		string name = Path.GetFileName(origin.Folder);
		string folder = Path.Combine(rig.Layout.Root, "projects", name);
		string hub = Path.Combine(folder, "hub");
		Project project = Assert.Single(await rig.Catalog.ListAsync(Token));
		Assert.Equal((name, folder, true), (project.Name, project.Folder, project.IsOpen));
		Assert.Equal(project.Id, rig.OpenProject.Current?.Id);
		Assert.Equal(["hub", "pyxie.db"], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
		Assert.Equal("true", await origin.RunGitAsync("-C", hub, "rev-parse", "--is-bare-repository"));

		await using ProjectDbContext database = ProjectDatabaseFile.Open(folder);
		Assert.Equal(["Feature", "Bug", "Chore"], await database.Categories.OrderBy(category => category.Id).Select(category => category.Name).ToListAsync(Token));
		Repository stored = await database.Repositories.AsNoTracking().SingleAsync(Token);
		Assert.Equal((connected.Id, source, hub, "main"), (stored.Id, stored.Source, stored.Path, stored.TargetBranch));
		Assert.Equal(Path.GetFullPath(origin.Folder), Path.GetFullPath(stored.Origin), ignoreCase: true);
		Assert.Equal(DateTimeKind.Utc, stored.ImportedUtc.Kind);
		Assert.InRange(stored.ImportedUtc, before, after);
	}

	[Fact]
	public async Task Import_OfASourceWhoseProjectIsRegistered_NamesTheNewProjectWithTheSuffixTwoAndLeavesTheFirstAlone()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));
		Project first = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(first.Id, Token));

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));

		string name = Path.GetFileName(origin.Folder) + "-2";
		string folder = Path.Combine(rig.Layout.ProjectsFolder, name);
		Assert.Equal((name, folder), (rig.OpenProject.Current?.Name, rig.OpenProject.Current?.Folder));
		Assert.Equal(WorkspaceLayout.HubPath(folder), connected.Path);
		Assert.Equal([(first.Name, false), (name, true)], (await rig.Catalog.ListAsync(Token)).Select(project => (project.Name, project.IsOpen)));
		Assert.True(File.Exists(DatabaseFiles.PathIn(first.Folder)) && Directory.Exists(WorkspaceLayout.HubPath(first.Folder)), "The first project keeps its database and its hub.");
	}

	[Fact]
	public async Task Import_OfASourceWhoseRegisteredProjectLostItsFolder_StillSkipsTheRegisteredName()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));
		Project first = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(first.Id, Token));
		await TempFolder.DeleteAsync(first.Folder);

		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));

		Assert.Equal(Path.GetFileName(origin.Folder) + "-2", rig.OpenProject.Current?.Name);
		Assert.False(Directory.Exists(first.Folder), "The registered project's folder is not reused.");
		Assert.Equal(2, (await rig.Catalog.ListAsync(Token)).Count);
	}

	[Fact]
	public async Task Import_OfASourceWhoseProjectFolderIsTaken_NamesTheNewProjectWithTheNextFreeNumber()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));
		Project first = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(first.Id, Token));
		Directory.CreateDirectory(rig.ProjectFolderFor(repository.Folder) + "-2");

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		string name = Path.GetFileName(repository.Folder) + "-3";
		Assert.Equal(WorkspaceLayout.HubPath(Path.Combine(rig.Layout.ProjectsFolder, name)), connected.Path);
		Assert.Equal(name, rig.OpenProject.Current?.Name);
		Assert.Equal(2, (await rig.Catalog.ListAsync(Token)).Count);
	}
}
