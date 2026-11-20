using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Cancel stops the build a person started: the engine is told, the page goes back to Build with nothing to say about the build, and Cancel is offered for a person's build only, not for one the pipeline runs.</summary>
public sealed class SandboxSettingsEditorBuildCancelTests
{
	[Fact]
	public async Task DelayedCancelLeavesTheReplacementBuildRunning()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		await editor.StartBuildAsync();
		await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		GateRenderedElement oldCancel = Assert.IsType<GateRenderedElement>(await editor.CancelButtonAsync());
		SandboxImageBuild first = Assert.IsType<SandboxImageBuild>(editor.ImageBuilds.PersonBuild);
		await editor.Host.HoldAcknowledgementsAsync();
		try
		{
			Assert.True(editor.ImageBuilds.Cancel(first.ProjectKey, first.Id), "The first build should accept cancellation.");
			await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
			Assert.Equal(SandboxImageBuildState.Cancelled, editor.ImageBuilds.Find(first.ProjectKey, first.Id)?.State);
			string otherFolder = editor.NewOtherProjectFolder();
			Assert.True(editor.ImageBuilds.TryStart(otherFolder, first.Dockerfile, out int replacement), "The second project should start its own build.");
			await editor.Host.ClickAsync(oldCancel);
			release.SetResult();
			await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
			Assert.Equal(SandboxImageBuildState.Succeeded, editor.ImageBuilds.Find(Pyxie.Runtime.Sandboxes.SandboxNames.ProjectKey(otherFolder), replacement)?.State);
		}
		finally
		{
			release.TrySetResult();
			await editor.Host.AcknowledgeAsync();
		}
	}

	[Fact]
	public async Task Cancel_StopsTheBuildInTheEngineAndTheEditorGoesBackToBuildWithNothingToSayAboutIt()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Hold = new TaskCompletionSource().Task;
		await editor.StartBuildAsync();
		await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		Assert.False(editor.Sandboxes.Cancelled.IsCompleted, "Nothing cancelled a build yet.");

		await editor.CancelAsync();

		await editor.Sandboxes.Cancelled.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		GateRenderedElement build = await editor.BuildButtonAsync();
		Assert.True(build.IsClickable, "A cancelled build can be started again.");
		Assert.False(SandboxEditorHost.IsLoading(build), "Nothing builds any more.");
		Assert.Null(await editor.CancelButtonAsync());
		await editor.AssertNoOutcomeAsync();
		Assert.False((await editor.SaveButtonAsync()).IsClickable, "Nothing was changed, so there is nothing to save.");
	}

	[Fact]
	public async Task Build_AfterACancelledBuild_StartsAgainAndShowsBuilt()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Hold = new TaskCompletionSource().Task;
		await editor.StartBuildAsync();
		await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		await editor.CancelAsync();
		await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		editor.Sandboxes.Hold = null;

		await editor.BuildAsync();

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal("pyxie-project-test", await editor.BuiltTagAsync());
		Assert.Equal(2, editor.Sandboxes.Requests.Count);
	}

	[Fact]
	public async Task Cancel_AfterLeavingAndComingBack_StopsTheBuildThatWasStartedBeforeLeaving()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Hold = new TaskCompletionSource().Task;
		await editor.StartBuildAsync();
		await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		await editor.ReopenAsync();

		await editor.CancelAsync();

		await editor.Sandboxes.Cancelled.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The page that came back shows the build cancelled.");
		Assert.Null(await editor.CancelButtonAsync());
		await editor.AssertNoOutcomeAsync();
	}

	[Fact]
	public async Task Cancel_OfABuildWaitingBehindThePipelinesBuild_LeavesThePipelinesBuildShownAsBuildingWithoutCancel()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		editor.Sandboxes.Ahead = new TaskCompletionSource().Task;
		await editor.StartBuildAsync();
		Task<ServiceResult<string>> pipelineBuild = editor.PipelineBuildAsync();
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			GateRenderedElement building = await editor.BuildButtonAsync("Building");
			Assert.True(SandboxEditorHost.IsLoading(building), "A build that waits for the pipeline's is building as far as a person is concerned.");
			Assert.True((await editor.CancelButtonAsync())?.IsClickable, "A person's build that waits can be cancelled.");

			await editor.CancelAsync();
			await editor.Sandboxes.Cancelled.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
			await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);

			GateRenderedElement stillBuilding = await editor.BuildButtonAsync("Building");
			Assert.True(SandboxEditorHost.IsLoading(stillBuilding), "The pipeline's build still runs.");
			Assert.False(stillBuilding.IsClickable, "A second build cannot start while one runs.");
			Assert.Null(await editor.CancelButtonAsync());
			await editor.AssertNoOutcomeAsync();
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(pipelineBuild);
		}

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Once the pipeline's build ended, Build can start another.");
		Assert.Null(await editor.CancelButtonAsync());
	}
}
