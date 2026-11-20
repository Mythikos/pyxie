using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Runtime.Projects;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>Opening and closing projects. Closing names the project and closes it only while it is the open one, and keeps everything; opening needs the project's folder, database, and hub; both are refused while the pipeline is started. Only one project is open at a time, so opening a project while another is open switches to it in one step, and each change that happened is announced once. A project that cannot be opened leaves the open project open and announces nothing.</summary>
public sealed class ProjectSessionsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CloseAsync_AfterAnImport_ClosesTheProjectButKeepsItInTheListWithItsHubAndTheSourceOnDisk()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));
		Project project = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");

		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(project.Id, Token));

		Assert.Null(rig.OpenProject.Current);
		Assert.Null(await rig.GetStoredAsync());
		Project closed = Assert.Single(await rig.Catalog.ListAsync(Token));
		Assert.False(closed.IsOpen, "The closed project stays in the list, closed.");
		Assert.True(Directory.Exists(connected.Path), "Closing leaves the hub where it is.");
		Assert.True(File.Exists(Path.Combine(repository.Folder, TempGitRepository.InitialFile)), "Closing never deletes the folder that was imported.");
	}

	[Fact]
	public async Task CloseAsync_OfAProjectThatDoesNotExist_SaysSoAndAnnouncesNothing()
	{
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.CloseAsync(42, Token));

		Assert.Equal("Project not found.", error);
		Assert.Equal(0, announced);
	}

	[Fact]
	public async Task CloseAsync_OfAClosedProjectWhenNoProjectIsOpen_SaysItIsNotOpenAndAnnouncesNothing()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project closed = await ImportAndCloseAsync(rig, repository);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.CloseAsync(closed.Id, Token));

		Assert.Equal("The project is not open.", error);
		Assert.Equal(0, announced);
		Assert.Null(rig.OpenProject.Current);
	}

	[Fact]
	public async Task CloseAsync_OfAProjectThatIsNotOpenWhileAnotherIsOpen_IsRefusedAnnouncesNothingAndLeavesTheOpenOneOpen()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.CloseAsync(first.Id, Token));

		Assert.Equal("The project is not open.", error);
		await AssertStillOpenAsync(rig, second, announced);
	}

	[Fact]
	public async Task CloseAsync_OfTheOpenProjectWhileAnotherIsListed_ClosesItAnnouncesItOnceAndLeavesTheOtherClosed()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(second.Id, Token));

		Assert.Equal(1, announced);
		Assert.Null(rig.OpenProject.Current);
		Assert.Equal([(first.Id, false), (second.Id, false)], (await rig.Catalog.ListAsync(Token)).OrderBy(project => project.Id).Select(project => (project.Id, project.IsOpen)));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CloseAsync_WhileThePipelineIsStarted_IsRefusedAnnouncesNothingAndKeepsTheProjectOpen()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		Project project = harness.OpenProject.Current ?? throw new InvalidOperationException("Connecting did not open a project.");
		await harness.StartPipelineAsync();
		int announced = 0;
		harness.Activity.ProjectChanged += () => announced++;
		await using AsyncServiceScope scope = harness.CreateScope();

		string error = ResultAssert.Failed(await scope.ServiceProvider.GetRequiredService<ProjectSessions>().CloseAsync(project.Id, Token));

		Assert.Equal("Stop the pipeline first.", error);
		Assert.Equal(0, announced);
		Assert.NotNull(harness.OpenProject.Current);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task OpenAsync_WhileThePipelineIsStarted_IsRefusedAndAnnouncesNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		Project project = harness.OpenProject.Current ?? throw new InvalidOperationException("Connecting did not open a project.");
		await harness.StartPipelineAsync();
		int announced = 0;
		harness.Activity.ProjectChanged += () => announced++;
		await using AsyncServiceScope scope = harness.CreateScope();

		string error = ResultAssert.Failed(await scope.ServiceProvider.GetRequiredService<ProjectSessions>().OpenAsync(project.Id, Token));

		Assert.Equal("Stop the pipeline first.", error);
		Assert.Equal(0, announced);
		Assert.Equal(project.Id, harness.OpenProject.Current?.Id);
	}

	[Fact]
	public async Task OpenAsync_OfAClosedProject_OpensItAgainWithItsRepository()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));
		Project project = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(project.Id, Token));

		ResultAssert.Succeeded(await rig.Sessions.OpenAsync(project.Id, Token));

		Assert.Equal(project.Id, rig.OpenProject.Current?.Id);
		Assert.Equal(connected.Path, (await rig.GetStoredAsync())?.Path);
	}

	[Fact]
	public async Task OpenAsync_OfTheProjectThatIsAlreadyOpen_SucceedsWithoutOpeningItAgainOrAnnouncingIt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		Project open = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		int announced = 0;
		int opened = 0;
		rig.Activity.ProjectChanged += () => announced++;
		rig.OpenProject.Changed += () => opened++;

		Project result = ResultAssert.Succeeded(await rig.Sessions.OpenAsync(open.Id, Token));

		Assert.Equal(open.Id, result.Id);
		await AssertStillOpenAsync(rig, open, announced);
		Assert.Equal(0, opened);
	}

	[Fact]
	public async Task OpenAsync_WhileAnotherProjectIsOpen_SwitchesToItAndAnnouncesTheSwitchOnce()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		int announced = 0;
		int opened = 0;
		rig.Activity.ProjectChanged += () => announced++;
		rig.OpenProject.Changed += () => opened++;

		Project switched = ResultAssert.Succeeded(await rig.Sessions.OpenAsync(first.Id, Token));

		Assert.Equal(first.Id, switched.Id);
		Assert.Equal((first.Id, first.Folder), (rig.OpenProject.Current?.Id, rig.OpenProject.Current?.Folder));
		Assert.Equal([(first.Id, true), (second.Id, false)], (await rig.Catalog.ListAsync(Token)).OrderBy(project => project.Id).Select(project => (project.Id, project.IsOpen)));
		Assert.Equal(rig.HubFor(firstSource.Folder), (await rig.GetStoredAsync())?.Path);
		Assert.Equal(1, announced);
		Assert.Equal(1, opened);
	}

	[Fact]
	public async Task OpenAsync_WhileAnotherProjectIsOpenAndThePipelineIsStarted_IsRefusedAndLeavesTheOpenOneOpen()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		ResultAssert.Succeeded(rig.Controller.Start());
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(first.Id, Token));

		Assert.Equal("Stop the pipeline first.", error);
		await AssertStillOpenAsync(rig, second, announced);
	}

	[Fact]
	public async Task OpenAsync_OfAProjectWhoseFolderIsGoneWhileAnotherIsOpen_IsRefusedAndLeavesTheOpenOneOpen()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		await TempFolder.DeleteAsync(first.Folder);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(first.Id, Token));

		Assert.Equal($"The folder {first.Folder} is gone.", error);
		await AssertStillOpenAsync(rig, second, announced);
	}

	[Fact]
	public async Task OpenAsync_OfAProjectThatDoesNotExistWhileAnotherIsOpen_SaysSoAndLeavesTheOpenOneOpen()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(_, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(42, Token));

		Assert.Equal("Project not found.", error);
		await AssertStillOpenAsync(rig, second, announced);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task OpenAsync_WhenTheProjectsDatabaseCannotBeMigratedWhileAnotherIsOpen_IsRefusedAndLeavesTheOpenOneOpen(bool fromAnotherBuild)
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, Project second) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);
		await ProjectDatabaseFile.MakeUnopenableAsync(first.Folder, fromAnotherBuild, Token);
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(first.Id, Token));

		Assert.Contains("database", error, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("import", error, StringComparison.OrdinalIgnoreCase);
		await AssertStillOpenAsync(rig, second, announced);
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(second.Id, Token));
		Assert.Null(rig.OpenProject.Current);
	}

	[Fact]
	public async Task OpenAsync_WhileAnotherProjectIsOpen_BringsTheProjectsDatabaseUpToDateAsItSwitches()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		(Project first, _) = await ImportClosedAndOpenAsync(rig, firstSource, secondSource);

		// An empty file is a valid, empty SQLite database: the project's migrations have not been applied to it.
		await File.WriteAllBytesAsync(DatabaseFiles.PathIn(first.Folder), [], Token);

		ResultAssert.Succeeded(await rig.Sessions.OpenAsync(first.Id, Token));

		await using ProjectDbContext database = ProjectDatabaseFile.Open(first.Folder);
		Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync(Token));
		Assert.Equal(first.Id, rig.OpenProject.Current?.Id);
	}

	[Fact]
	public async Task CloseThenOpen_SwitchesToTheOtherProjectAndItsOwnRepositoryAnnouncingEachChange()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project first = await ImportAndCloseAsync(rig, firstSource);
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(secondSource.Folder, Token));
		Project second = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;
		Task nextChange = rig.Activity.NextChangeAsync();

		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(second.Id, Token));

		Assert.Equal(1, announced);
		Assert.True(nextChange.IsCompleted, "Closing wakes idle pipeline workers too.");
		Assert.Null(await rig.GetStoredAsync());

		Project opened = ResultAssert.Succeeded(await rig.Sessions.OpenAsync(first.Id, Token));

		Assert.Equal(2, announced);
		Assert.Equal((first.Id, first.Folder), (opened.Id, rig.OpenProject.Current?.Folder));
		Repository stored = await rig.GetStoredAsync() ?? throw new InvalidOperationException("The reopened project has no repository.");
		Assert.Equal(rig.HubFor(firstSource.Folder), stored.Path);
		Assert.Equal(Path.GetFullPath(firstSource.Folder), stored.Origin, ignoreCase: true);
	}

	[Fact]
	public async Task OpenAsync_OfAProjectWhoseFolderIsGone_IsRefusedAndLeavesItClosed()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, repository);
		await TempFolder.DeleteAsync(project.Folder);

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(project.Id, Token));

		Assert.Equal($"The folder {project.Folder} is gone.", error);
		Assert.Null(rig.OpenProject.Current);
		Assert.False(ProjectSessions.IsFolderPresent(project), "A project without its folder cannot be opened.");
	}

	[Theory]
	[InlineData("pyxie.db")]
	[InlineData("hub")]
	public async Task OpenAsync_OfAProjectMissingItsDatabaseOrItsHub_IsRefusedAsGoneAndAnnouncesNothing(string missing)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project project = await ImportAndCloseAsync(rig, repository);
		string path = Path.Combine(project.Folder, missing);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		else
		{
			await TempFolder.DeleteAsync(path);
		}

		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(project.Id, Token));

		Assert.Equal($"The folder {project.Folder} is gone.", error);
		Assert.Null(rig.OpenProject.Current);
		Assert.False(ProjectSessions.IsFolderPresent(project), $"A project without its {missing} cannot be opened.");
		Assert.Equal(0, announced);
	}

	[Fact]
	public async Task OpenAsync_OfAProjectThatDoesNotExist_SaysSo()
	{
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(42, Token));

		Assert.Equal("Project not found.", error);
		Assert.Null(rig.OpenProject.Current);
	}

	private static async Task<Project> ImportAndCloseAsync(RepositoryRig rig, TempGitRepository source)
	{
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		Project project = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(project.Id, Token));

		return project;
	}

	// Imports one project and closes it, then imports another, which stays open.
	private static async Task<(Project Closed, Project Open)> ImportClosedAndOpenAsync(RepositoryRig rig, TempGitRepository closedSource, TempGitRepository openSource)
	{
		Project closed = await ImportAndCloseAsync(rig, closedSource);
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(openSource.Folder, Token));
		Project open = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");

		return (closed, open);
	}

	// What a refused or failed open leaves behind: the open project still open, in memory and in the catalog, and nothing announced.
	private static async Task AssertStillOpenAsync(RepositoryRig rig, Project open, int announced)
	{
		Assert.Equal(0, announced);
		Assert.Equal((open.Id, open.Folder), (rig.OpenProject.Current?.Id, rig.OpenProject.Current?.Folder));
		Assert.Equal(open.Id, (await rig.Catalog.GetOpenAsync(Token))?.Id);
	}
}
