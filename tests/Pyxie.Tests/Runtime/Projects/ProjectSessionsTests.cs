using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Runtime.Projects;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>Opening and closing projects. Closing keeps everything; opening needs the project's folder, database, and hub; both are refused while the pipeline is started, only one project is open at a time, so switching is a close followed by an open, and each change that happened is announced once.</summary>
public sealed class ProjectSessionsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CloseAsync_AfterAnImport_ClosesTheProjectButKeepsItInTheListWithItsHubAndTheSourceOnDisk()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(Token));

		Assert.Null(rig.OpenProject.Current);
		Assert.Null(await rig.GetStoredAsync());
		Project closed = Assert.Single(await rig.Catalog.ListAsync(Token));
		Assert.False(closed.IsOpen, "The closed project stays in the list, closed.");
		Assert.True(Directory.Exists(connected.Path), "Closing leaves the hub where it is.");
		Assert.True(File.Exists(Path.Combine(repository.Folder, TempGitRepository.InitialFile)), "Closing never deletes the folder that was imported.");
	}

	[Fact]
	public async Task CloseAsync_WhenNoProjectIsOpen_SaysSoAndAnnouncesNothing()
	{
		await using RepositoryRig rig = new();
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.CloseAsync(Token));

		Assert.Equal("No project is open.", error);
		Assert.Equal(0, announced);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CloseAsync_WhileThePipelineIsStarted_IsRefusedAnnouncesNothingAndKeepsTheProjectOpen()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		await harness.StartPipelineAsync();
		int announced = 0;
		harness.Activity.ProjectChanged += () => announced++;
		await using AsyncServiceScope scope = harness.CreateScope();

		string error = ResultAssert.Failed(await scope.ServiceProvider.GetRequiredService<ProjectSessions>().CloseAsync(Token));

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
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(Token));

		ResultAssert.Succeeded(await rig.Sessions.OpenAsync(project.Id, Token));

		Assert.Equal(project.Id, rig.OpenProject.Current?.Id);
		Assert.Equal(connected.Path, (await rig.GetStoredAsync())?.Path);
	}

	[Fact]
	public async Task OpenAsync_WhileAnotherProjectIsOpen_IsRefusedAndKeepsTheOpenOne()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project first = await ImportAndCloseAsync(rig, firstSource);
		Repository second = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(secondSource.Folder, Token));
		Project open = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;

		string error = ResultAssert.Failed(await rig.Sessions.OpenAsync(first.Id, Token));

		Assert.Equal("Another project is open. Close it first.", error);
		Assert.Equal(open.Id, rig.OpenProject.Current?.Id);
		Assert.Equal(second.Path, (await rig.GetStoredAsync())?.Path);
		Assert.Equal(0, announced);
	}

	[Fact]
	public async Task CloseThenOpen_SwitchesToTheOtherProjectAndItsOwnRepositoryAnnouncingEachChange()
	{
		await using TempGitRepository firstSource = await TempGitRepository.CreateAsync();
		await using TempGitRepository secondSource = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Project first = await ImportAndCloseAsync(rig, firstSource);
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(secondSource.Folder, Token));
		int announced = 0;
		rig.Activity.ProjectChanged += () => announced++;
		Task nextChange = rig.Activity.NextChangeAsync();

		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(Token));

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
		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(Token));

		return project;
	}
}
