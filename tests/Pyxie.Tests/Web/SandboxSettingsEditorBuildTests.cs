using System.Globalization;

using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Build rebuilds the project's image from the saved Dockerfile. It waits for unsaved changes to be saved and says so, says when the image was built, and when the build fails or Docker does not answer it says why, with the end of the build's log where docker explains what went wrong.</summary>
public sealed class SandboxSettingsEditorBuildTests
{
	private const string Saved = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make\n";
	private const string Typed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make cmake\n";
	private const string BuildFailed = "The project's image could not be built. The log says why.";
	private const string SaveFirst = "Save the Dockerfile first.";

	[Fact]
	public async Task Build_WithUnsavedChanges_IsDisabledAndSaysToSaveFirstUntilTheyAreSaved()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The saved Dockerfile can be built.");
		Assert.Null(await editor.BuildTitleAsync());

		await editor.TypeDockerfileAsync(Typed);

		Assert.False((await editor.BuildButtonAsync()).IsClickable, "A build uses the saved Dockerfile, so unsaved changes have to be saved first.");
		Assert.Equal(SaveFirst, await editor.BuildTitleAsync());

		await editor.SaveAsync();

		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Once the changes are saved, they can be built.");
		Assert.Null(await editor.BuildTitleAsync());
	}

	[Fact]
	public async Task Build_ThatSucceeds_SaysSoAndRebuildsTheSavedDockerfileIntoTheProjectsImageWithItsLogInTheProjectFolder()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		await editor.BuildAsync();

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal("pyxie-project-test", await editor.BuiltTagAsync());
		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
		SandboxImageRequest request = Assert.Single(editor.Sandboxes.Requests);
		Assert.Equal(SandboxNames.ProjectKey(editor.ProjectFolder), request.ProjectKey);
		Assert.Equal(Saved, request.Dockerfile);
		Assert.True(request.Rebuild, "Build rebuilds from scratch, which also brings the agents' command lines in the base image up to date.");
		Assert.Equal(WorkspaceLayout.ImageLogPath(editor.ProjectFolder), request.LogPath);
	}

	[Fact]
	public async Task Build_ThatSucceeds_ShowsTheTagOfTheImageItMade()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Success("pyxie-project-app:3f9a2c1e");

		await editor.BuildAsync();

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal("pyxie-project-app:3f9a2c1e", await editor.BuiltTagAsync());
	}

	[Fact]
	public async Task Build_ThatSucceedsAfterOneThatFailed_ShowsTheTagAndNoLog()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = ["ERROR: failed to solve"];
		await editor.BuildAsync();

		editor.Sandboxes.Result = ServiceResult<string>.Success("pyxie-project-app:1");
		await editor.BuildAsync();

		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
		Assert.Equal("pyxie-project-app:1", await editor.BuiltTagAsync());
	}

	[Fact]
	public async Task Build_ThatFails_ShowsTheLogInAMonospaceBlockAndNoBuiltStatus()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = ["ERROR: failed to solve"];

		await editor.BuildAsync();

		Assert.Single(await editor.Host.FindElementsAsync("pre", block => block.Attribute("class") == "sandbox-build-log"));
		Assert.Null(await editor.BuiltStatusAsync());
	}

	[Fact]
	public async Task Build_WithNothingSaved_BuildsTheDefaultDockerfile()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		await editor.BuildAsync();

		Assert.Equal(SandboxDefaults.Dockerfile, Assert.Single(editor.Sandboxes.Requests).Dockerfile);
	}

	[Fact]
	public async Task Build_ThatFails_ShowsTheErrorAndTheLastTwentyLinesOfTheLog()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		string[] log = [.. Enumerable.Range(1, 25).Select(line => string.Create(CultureInfo.InvariantCulture, $"step {line:00}"))];
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = log;

		await editor.BuildAsync();

		Assert.Equal([BuildFailed], await editor.Host.AlertsAsync());
		Assert.Equal(string.Join('\n', log[5..]), await editor.BuildLogAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
	}

	[Fact]
	public async Task Build_ThatFailsWithAShortLog_ShowsAllOfIt()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = ["#1 [internal] load build definition from Dockerfile", "ERROR: failed to solve: process \"/bin/sh -c make\" did not complete successfully: exit code: 2"];

		await editor.BuildAsync();

		Assert.Equal(string.Join('\n', editor.Sandboxes.LogLines), await editor.BuildLogAsync());
	}

	[Fact]
	public async Task Build_ThatFailsBeforeWritingALog_ShowsTheErrorAlone()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);

		await editor.BuildAsync();

		Assert.Equal([BuildFailed], await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
	}

	[Fact]
	public async Task Build_WhenDockerDoesNotAnswer_ShowsWhyAndCanBeTriedAgain()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		const string Unavailable = "Docker is not running. Start Docker Desktop and try again.";
		editor.Sandboxes.UnavailableMessage = Unavailable;

		await editor.BuildAsync();

		Assert.Equal([Unavailable], await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
		Assert.Null(await editor.BuildLogAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "A build Docker refused can be tried again.");

		editor.Sandboxes.UnavailableMessage = null;
		await editor.BuildAsync();

		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Equal("Built", await editor.BuiltStatusAsync());
	}

	[Fact]
	public async Task Save_AfterAFailedBuild_ClearsTheErrorAndTheLogBecauseTheyDescribeTheOldDockerfile()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = ["ERROR: failed to solve"];
		await editor.BuildAsync();
		Assert.NotNull(await editor.BuildLogAsync());

		await editor.TypeDockerfileAsync(Typed);
		await editor.SaveAsync();

		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
	}

	[Fact]
	public async Task Save_AfterASuccessfulBuild_ClearsTheBuiltMessage()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		await editor.BuildAsync();
		Assert.Equal("Built", await editor.BuiltStatusAsync());

		await editor.TypeDockerfileAsync(Typed);
		await editor.SaveAsync();

		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
	}

	[Fact]
	public async Task Build_WhileItRuns_KeepsBuildAndSaveDisabled()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;

		Task build = editor.BuildAsync();
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
			await editor.TypeDockerfileAsync(Typed);

			Assert.False((await editor.BuildButtonAsync()).IsClickable, "A second build cannot start while one runs.");
			Assert.False((await editor.SaveButtonAsync()).IsClickable, "The Dockerfile being built cannot be replaced while the build runs.");
		}
		finally
		{
			release.TrySetResult();
			await build;
		}

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.True((await editor.SaveButtonAsync()).IsClickable, "Once the build ended, the change typed meanwhile can be saved.");
		Assert.Single(editor.Sandboxes.Requests);
	}

	[Fact]
	public async Task Build_AfterTheProjectWasClosed_BuildsNothing()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.OpenProject.Set(null);

		await editor.BuildAsync();

		Assert.Empty(editor.Sandboxes.Requests);
		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
	}

	private static async Task SaveElsewhereAsync(TestDatabase database, string dockerfile)
	{
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(dockerfile), 0, TestContext.Current.CancellationToken));
	}
}
