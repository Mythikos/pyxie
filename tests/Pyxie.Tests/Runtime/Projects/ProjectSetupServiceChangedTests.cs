using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>The header's Start button and the pages follow <c>PipelineActivity.ProjectChanged</c>, so it is raised once an import is over whenever the import put a project in the catalog: after a project was imported, and after an import that failed once the project was listed, because a page may have listed it meanwhile. An import that was refused or failed earlier announces nothing. Opening and closing announce too (<see cref="ProjectSessionsTests"/>).</summary>
public sealed class ProjectSetupServiceChangedTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ImportFolderAsync_WhenTheImportSucceeds_AnnouncesThatTheProjectChangedOnce()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		Assert.Equal(1, announced);
		Assert.NotNull(await rig.GetStoredAsync());
	}

	[Fact]
	public async Task ImportFolderAsync_WhenTheImportSucceeds_WakesWorkersThatWaitForAChange()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Task nextChange = rig.Activity.NextChangeAsync();

		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		Assert.True(nextChange.IsCompleted, "Importing a project wakes idle pipeline workers, which would otherwise wait for the next poll.");
	}

	[Fact]
	public async Task ImportFolderAsync_WhenTheImportIsRefused_AnnouncesNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using TempGitRepository second = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;
		string missing = Path.Combine(source.Folder, "no-such-folder");

		ResultAssert.Failed(await rig.Setup.ImportFolderAsync(string.Empty, Token));
		ResultAssert.Failed(await rig.Setup.ImportFolderAsync("relative", Token));
		ResultAssert.Failed(await rig.Setup.ImportFolderAsync(missing, Token));
		ResultAssert.Failed(await rig.Setup.ImportFolderAsync(second.Folder, Token));

		Assert.Equal(0, announced);
	}

	[Fact]
	public async Task ImportFolderAsync_WhenTheRepositoryServiceRefusesTheConnection_AnnouncesOnceTheProjectIsGoneAgain()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new(repositories: new RefusingRepositoryService());
		int announced = 0;
		bool listedWhenAnnounced = true;
		rig.Activity.ProjectChanged += () =>
		{
			announced++;
			listedWhenAnnounced = rig.OpenProject.Current is not null;
		};

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		Assert.Equal(RefusingRepositoryService.Reason, error);
		Assert.Equal(1, announced);
		Assert.False(listedWhenAnnounced, "The announcement comes after the project was closed and removed again, so a page that reads then no longer shows it.");
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task ImportFolderAsync_WhenTheFolderHasNoBranchToStartFrom_AnnouncesNothing()
	{
		await using EmptyGitRepository empty = await EmptyGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		ResultAssert.Failed(await rig.Setup.ImportFolderAsync(empty.Folder, Token));

		Assert.Equal(0, announced);
		Assert.Null(await rig.GetStoredAsync());
	}

	[Fact]
	public async Task CloneAsync_WhenTheCloneFails_AnnouncesNothing()
	{
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		ResultAssert.Failed(await rig.Setup.CloneAsync("http://127.0.0.1:1/owner/repo.git", Token));

		Assert.Equal(0, announced);
	}

	[Fact]
	public async Task CloneAsync_WhenTheCloneSucceeds_AnnouncesThatTheProjectChangedOnce()
	{
		await using ScratchFolder served = ScratchFolder.Create();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		string bare = served.Combine("repo.git");
		await source.RunGitAsync("clone", "--bare", "--quiet", ".", bare);
		await source.RunGitAsync("-C", bare, "update-server-info");
		await using StaticFileHttpServer server = await StaticFileHttpServer.StartAsync(served.Folder);
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		Repository connected = ResultAssert.Succeeded(await rig.Setup.CloneAsync($"{server.BaseAddress}/repo.git", Token));

		Assert.Equal(1, announced);
		Assert.Equal((RepositorySource.Url, "main"), (connected.Source, connected.TargetBranch));
	}
}
