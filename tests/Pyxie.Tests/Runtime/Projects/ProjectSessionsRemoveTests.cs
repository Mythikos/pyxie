using Pyxie.Data.Entities;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>Removing a project that is not open: a person's build of its image is stopped, its sandboxes and images go, then its folder, and last its entry, and the removal is announced. A project's key is a hash of its folder, and importing the same source again gives the same folder, so the project imported then finds nothing of the removed one. Removing is refused for the open project, while the pipeline is started, without Docker, and for a folder outside the projects folder, and then nothing is deleted; a folder that cannot be deleted leaves the entry, and removing it again finishes. The source is never touched.</summary>
public sealed class ProjectSessionsRemoveTests
{
	private const string Dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\n";
	private const string DockerDownMessage = "Docker is not running, so the project's sandboxes cannot be removed. Start Docker Desktop, then remove the project again.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RemoveAsync_OfAClosedProject_RemovesItsSandboxesFolderAndEntryAnnouncesItOnceAndLeavesTheSource()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, source);
		string key = SandboxNames.ProjectKey(project.Folder);
		await LeaveSandboxesAsync(rig, key);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		ResultAssert.Succeeded(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.False(Directory.Exists(project.Folder), "The project's folder, with its database and hub, should be deleted, also right after the project was open.");
		Assert.Empty(await rig.Catalog.ListAsync(Token));
		Assert.Equal([key], rig.Sandboxes.ProjectRemovals);
		Assert.Empty(await rig.Sandboxes.ListCardsAsync(key, Token));
		Assert.Null(await rig.Sandboxes.FindImageAsync(key, Dockerfile, Token));
		Assert.Equal(1, announced);
		Assert.Null(rig.OpenProject.Current);
		Assert.True(File.Exists(Path.Combine(source.Folder, TempGitRepository.InitialFile)), "Removing a project never touches the folder it was imported from.");
	}

	[Fact]
	public async Task RemoveAsync_ThenImportingTheSameSourceAgain_GivesTheNewProjectTheSameFolderAndKeyWithNothingOfTheRemovedOne()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project removed = await ImportAndCloseAsync(rig, source);
		string key = SandboxNames.ProjectKey(removed.Folder);
		await LeaveSandboxesAsync(rig, key);
		Assert.True(rig.ImageBuilds.TryStart(removed.Folder, Dockerfile), "The test needs a build of the project's image.");
		await rig.ImageBuilds.Running;
		Assert.NotNull(rig.ImageBuilds.Latest(key));
		ResultAssert.Succeeded(await rig.Sessions.RemoveAsync(removed.Id, Token));

		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		Project imported = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		Assert.Equal(removed.Folder, imported.Folder, ignoreCase: true);
		Assert.Equal(key, SandboxNames.ProjectKey(imported.Folder));
		Assert.Empty(await rig.Sandboxes.ListCardsAsync(key, Token));
		Assert.Null(await rig.Sandboxes.FindImageAsync(key, Dockerfile, Token));
		Assert.Null(rig.ImageBuilds.Latest(key));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveAsync_WhileAPersonsBuildOfTheProjectsImageRuns_CancelsTheBuildAndRemovesTheProject()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, source);
		TaskCompletionSource began = new(TaskCreationOptions.RunContinuationsAsynchronously);
		rig.Sandboxes.EnsureImage = async (request, cancellationToken) =>
		{
			began.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);

			return ServiceResult<string>.Failure("The build was never cancelled.");
		};
		Assert.True(rig.ImageBuilds.TryStart(project.Folder, Dockerfile), "The test needs a build of the project's image.");
		await began.Task.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		ResultAssert.Succeeded(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.True(rig.ImageBuilds.Running.IsCompleted, "The build should have ended before the project's images were removed.");
		Assert.Null(rig.ImageBuilds.PersonBuild);
		Assert.Null(rig.ImageBuilds.Latest(SandboxNames.ProjectKey(project.Folder)));
		Assert.False(Directory.Exists(project.Folder), "The project's folder should be deleted.");
	}

	[Fact]
	public async Task RemoveAsync_OfTheOpenProject_IsRefusedAndKeepsEverything()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		Project open = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(open.Id, Token));

		Assert.Equal("Close the project first.", error);
		await AssertKeptAsync(rig, open, announced);
		Assert.Equal(open.Id, rig.OpenProject.Current?.Id);
	}

	[Fact]
	public async Task RemoveAsync_OfAProjectThatDoesNotExist_SaysSoAndAnnouncesNothing()
	{
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(42, Token));

		Assert.Equal("Project not found.", error);
		Assert.Equal(0, announced);
		Assert.Empty(rig.Sandboxes.ProjectRemovals);
	}

	[Fact]
	public async Task RemoveAsync_WhileThePipelineIsStarted_IsRefusedAndKeepsEverything()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project closed = await ImportAndCloseAsync(rig, firstSource);
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(secondSource.Folder, Token));
		ResultAssert.Succeeded(rig.Controller.Start());
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(closed.Id, Token));

		Assert.Equal("Stop the pipeline first.", error);
		await AssertKeptAsync(rig, closed, announced, listed: 2);
	}

	[Fact]
	public async Task RemoveAsync_WhenDockerIsNotRunning_IsRefusedKeepsTheFolderAndEntryAndRemovingAgainOnceItRunsFinishes()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, source);
		rig.Sandboxes.RemoveProjectFailure = new SandboxUnavailableException("Docker is not running.");
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.Equal(DockerDownMessage, error);
		await AssertKeptAsync(rig, project, announced, removals: 1);

		rig.Sandboxes.RemoveProjectFailure = null;
		ResultAssert.Succeeded(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.False(Directory.Exists(project.Folder), "Removing again once Docker runs should delete the folder.");
		Assert.Empty(await rig.Catalog.ListAsync(Token));
	}

	[Fact]
	public async Task RemoveAsync_WhenDockerCannotRemoveTheSandboxes_IsRefusedWithDockersReasonAndKeepsTheFolderAndEntry()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, source);
		rig.Sandboxes.RemoveProjectFailure = new SandboxException("docker volume failed: the volume is in use");
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.Equal("The project's sandboxes could not be removed: docker volume failed: the volume is in use", error);
		await AssertKeptAsync(rig, project, announced, removals: 1);
	}

	[Theory]
	[InlineData("outside the workspace")]
	[InlineData("the projects folder itself")]
	[InlineData("inside another project's folder")]
	public async Task RemoveAsync_OfAProjectWhoseFolderIsNotOneOfThePyxieProjectFolders_IsRefusedAndDeletesNothing(string where)
	{
		await using ScratchFolder elsewhere = ScratchFolder.Create("pyxie-elsewhere");
		await using RepositoryRig rig = new();
		string folder = where switch
		{
			"outside the workspace" => elsewhere.Folder,
			"the projects folder itself" => rig.Layout.ProjectsFolder,
			_ => Path.Combine(rig.Layout.ProjectsFolder, "app", "nested"),
		};
		Directory.CreateDirectory(folder);
		string marker = Path.Combine(folder, "keep.txt");
		await File.WriteAllTextAsync(marker, "kept", Token);
		Project project = ResultAssert.Succeeded(await rig.Catalog.AddAsync("stray", folder, "https://example.test/stray.git", Token));
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.Equal($"The folder {folder} is not in Pyxie's projects folder, so Pyxie does not delete it.", error);
		Assert.True(File.Exists(marker), $"Nothing in a folder that is {where} may be deleted.");
		Assert.Single(await rig.Catalog.ListAsync(Token));
		Assert.Empty(rig.Sandboxes.ProjectRemovals);
		Assert.Equal(0, announced);
	}

	[Fact]
	public async Task RemoveAsync_OfAProjectWhoseFolderIsGone_RemovesItsSandboxesAndEntry()
	{
		await using RepositoryRig rig = new();
		string folder = Path.Combine(rig.Layout.ProjectsFolder, "moved");
		Project project = ResultAssert.Succeeded(await rig.Catalog.AddAsync("moved", folder, "https://example.test/moved.git", Token));
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		ResultAssert.Succeeded(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.Empty(await rig.Catalog.ListAsync(Token));
		Assert.Equal([SandboxNames.ProjectKey(folder)], rig.Sandboxes.ProjectRemovals);
		Assert.Equal(1, announced);
	}

	[Fact]
	public async Task RemoveAsync_WhenAFileInTheFolderIsInUse_IsRefusedKeepsTheEntryAnnouncesItAndRemovingAgainFinishes()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, source);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error;
		await using (FileStream held = new(Path.Combine(project.Folder, "held.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
		{
			error = ResultAssert.Failed(await rig.Sessions.RemoveAsync(project.Id, Token));
		}

		Assert.StartsWith($"The folder {project.Folder} could not be deleted: ", error, StringComparison.Ordinal);
		Assert.EndsWith(" Close whatever has a file in it open, then remove the project again.", error, StringComparison.Ordinal);
		Assert.Single(await rig.Catalog.ListAsync(Token));
		Assert.Equal(1, announced);

		ResultAssert.Succeeded(await rig.Sessions.RemoveAsync(project.Id, Token));

		Assert.False(Directory.Exists(project.Folder), "Removing again should finish deleting the folder.");
		Assert.Empty(await rig.Catalog.ListAsync(Token));
		Assert.Equal(2, announced);
	}

	private static async Task<Project> ImportAndCloseAsync(RepositoryRig rig, TempGitRepository source)
	{
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		Project project = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(project.Id, Token));

		return project;
	}

	// What a project's rounds and builds leave in the stand-in engine: a card's worker sandbox and the project's image.
	private static async Task LeaveSandboxesAsync(RepositoryRig rig, string projectKey)
	{
		Directory.CreateDirectory(rig.Sandboxes.WorkFolder(SandboxNames.Container(projectKey, 1, SandboxRole.Worker)));
		ResultAssert.Succeeded(await rig.Sandboxes.EnsureImageAsync(new SandboxImageRequest(projectKey, Dockerfile, Rebuild: false, Path.Combine(rig.Layout.Root, "image.log")), Token));
		Assert.Equal([1], await rig.Sandboxes.ListCardsAsync(projectKey, Token));
	}

	private static async Task AssertKeptAsync(RepositoryRig rig, Project project, int announced, int listed = 1, int removals = 0)
	{
		Assert.True(ProjectSessions.IsFolderPresent(project), "A refused removal must leave the project's folder, database, and hub.");
		Assert.Equal(listed, (await rig.Catalog.ListAsync(Token)).Count);
		Assert.Contains(await rig.Catalog.ListAsync(Token), listedProject => listedProject.Id == project.Id);
		Assert.Equal(removals, rig.Sandboxes.ProjectRemovals.Count);
		Assert.Equal(0, announced);
	}
}
