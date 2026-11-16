using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A build of the project's image belongs to the image builds, not to the page. Leaving the settings and coming back shows the build that is still running and then how it ended, a page that was left stops listening, and a page follows a build it did not start itself, without a click.</summary>
public sealed class SandboxSettingsEditorBuildLifetimeTests
{
	private const string BuildFailed = "The project's image could not be built. The log says why.";

	[Fact]
	public async Task Reopen_WhileABuildRuns_ShowsItAsBuildingWithCancelAndThenAsBuiltOnceItEnds()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		editor.Sandboxes.Result = ServiceResult<string>.Success("pyxie-project-app:7");
		await editor.StartBuildAsync();
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			await editor.ReopenAsync();

			GateRenderedElement building = await editor.BuildButtonAsync("Building");
			Assert.True(SandboxEditorHost.IsLoading(building), "The page that came back shows the build that kept running as loading.");
			Assert.False(building.IsClickable, "A second build cannot start while one runs.");
			Assert.True((await editor.CancelButtonAsync())?.IsClickable, "The build a person started before leaving can still be cancelled.");
			await editor.AssertNoOutcomeAsync();
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		}

		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal("pyxie-project-app:7", await editor.BuiltTagAsync());
		Assert.Null(await editor.CancelButtonAsync());
		GateRenderedElement idle = await editor.BuildButtonAsync();
		Assert.False(SandboxEditorHost.IsLoading(idle), "Once the build ended, Build stops loading.");
		Assert.True(idle.IsClickable, "Once the build ended, another can start.");
		Assert.Single(editor.Sandboxes.Requests);
	}

	[Fact]
	public async Task Reopen_LeavesOnlyTheNewEditorListeningToTheBuilds()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		Assert.Equal(1, EventSubscribers.Count(editor.ImageBuilds, nameof(SandboxImageBuilds.Changed)));

		await editor.ReopenAsync();

		Assert.Equal(1, EventSubscribers.Count(editor.ImageBuilds, nameof(SandboxImageBuilds.Changed)));

		await editor.Host.CloseAsync();

		Assert.Equal(0, EventSubscribers.Count(editor.ImageBuilds, nameof(SandboxImageBuilds.Changed)));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Render_WhenABuildBeginsAndEndsElsewhere_UpdatesItselfWithoutAClick(bool fails)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		if (fails)
		{
			editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
			editor.Sandboxes.LogLines = ["ERROR: failed to solve"];
		}

		Assert.Null(await editor.CancelButtonAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The editor shows Build before the build begins.");
		Assert.True(editor.ImageBuilds.TryStart(editor.ProjectFolder, SandboxDefaults.Dockerfile), "Nothing builds, so the build starts.");
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			GateRenderedElement building = await editor.BuildButtonAsync("Building");
			Assert.True(SandboxEditorHost.IsLoading(building), "The editor shows a build that began without a click.");
			Assert.False(building.IsClickable, "A second build cannot start while one runs.");
			Assert.True((await editor.CancelButtonAsync())?.IsClickable, "A person's build of this project can be cancelled from this page.");
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		}

		Assert.Null(await editor.CancelButtonAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The editor shows Build again once the build ended, without a click.");
		if (fails)
		{
			Assert.Equal([BuildFailed], await editor.Host.AlertsAsync());
			Assert.Equal("ERROR: failed to solve", await editor.BuildLogAsync());
			Assert.Null(await editor.BuiltStatusAsync());
		}
		else
		{
			Assert.Equal("Built", await editor.BuiltStatusAsync());
			Assert.Empty(await editor.Host.AlertsAsync());
		}
	}
}
