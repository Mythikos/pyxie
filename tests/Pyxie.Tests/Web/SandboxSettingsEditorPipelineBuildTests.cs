using System.Globalization;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The settings page shows the builds the pipeline makes for a round whose image is not there yet: it says the image is building, offers no Cancel because a person cannot cancel the pipeline's build, and shows how the build ended while it describes the saved Dockerfile.</summary>
public sealed class SandboxSettingsEditorPipelineBuildTests
{
	private const string Typed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make cmake\n";
	private const string BuildFailed = "The project's image could not be built. The log says why.";

	[Fact]
	public async Task Render_WhileThePipelineBuildsTheProjectsImage_ShowsBuildingWithoutCancelAndWithoutAClick()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The editor shows Build before the pipeline builds.");
		Task<ServiceResult<string>> pipelineBuild = editor.PipelineBuildAsync();
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			GateRenderedElement building = await editor.BuildButtonAsync("Building");
			Assert.True(SandboxEditorHost.IsLoading(building), "The pipeline's build of this project's image shows as loading.");
			Assert.False(building.IsClickable, "A second build cannot start while one runs.");
			Assert.Null(await editor.CancelButtonAsync());
			Assert.Null(await editor.BuildTitleAsync());
			await editor.AssertNoOutcomeAsync();
			Assert.False(Assert.Single(editor.Sandboxes.Requests).Rebuild, "The pipeline builds an image that is not there yet; it does not rebuild one.");
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(pipelineBuild);
		}
	}

	[Fact]
	public async Task Render_WhenThePipelinesBuildSucceeded_ShowsBuiltWithTheTagAndBuildCanStartAgain()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Success("pyxie-project-app:3f9a2c1e");

		await editor.PipelineBuildAsync();

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal("pyxie-project-app:3f9a2c1e", await editor.BuiltTagAsync());
		Assert.Empty(await editor.Host.AlertsAsync());
		GateRenderedElement build = await editor.BuildButtonAsync();
		Assert.True(build.IsClickable, "Once the pipeline's build ended, a person can build.");
		Assert.False(SandboxEditorHost.IsLoading(build), "Nothing builds any more.");
		Assert.Null(await editor.CancelButtonAsync());
	}

	[Fact]
	public async Task Render_WhenThePipelinesBuildFailed_ShowsTheErrorAndTheLastTwentyLinesOfTheLog()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		string[] log = [.. Enumerable.Range(1, 25).Select(line => string.Create(CultureInfo.InvariantCulture, $"step {line:00}"))];
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = log;

		ServiceResult<string> result = await editor.PipelineBuildAsync();

		Assert.False(result.Succeeded, "The stand-in failed the build.");
		Assert.Equal([BuildFailed], await editor.Host.AlertsAsync());
		Assert.Equal(string.Join('\n', log[5..]), await editor.BuildLogAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "A person can build again after the pipeline's build failed.");
	}

	[Fact]
	public async Task Render_WhenDockerDidNotAnswerThePipelinesBuild_ShowsWhyWithoutALog()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		const string Unavailable = "Docker is not running. Start Docker Desktop and try again.";
		editor.Sandboxes.UnavailableMessage = Unavailable;

		await Assert.ThrowsAsync<SandboxUnavailableException>(() => editor.PipelineBuildAsync());

		Assert.Equal([Unavailable], await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "A build Docker refused can be tried again.");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Render_WhenThePipelinesBuildWasOfADockerfileThatIsNotTheSavedOne_ShowsNoOutcome(bool fails)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		if (fails)
		{
			editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
			editor.Sandboxes.LogLines = ["ERROR: failed to solve"];
		}

		await editor.PipelineBuildAsync(dockerfile: Typed);

		await editor.AssertNoOutcomeAsync();
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The pipeline's build ended, so a person can build the saved Dockerfile.");
	}
}
