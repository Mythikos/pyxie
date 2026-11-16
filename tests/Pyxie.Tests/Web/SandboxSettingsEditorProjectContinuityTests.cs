using Pyxie.Data.Entities;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A project's settings page keeps showing the project's own image, Built with its tag, or the failure of its last build with the end of its log, while other projects build and after, and when a person opens another project and comes back; a rebuild shows only itself while it runs, and cancelling it brings back what was shown before.</summary>
public sealed class SandboxSettingsEditorProjectContinuityTests
{
	private const string AppTag = "pyxie-project-app:a1f0";
	private const string OtherTag = "pyxie-project-other:b2e1";
	private const string RebuildTag = "pyxie-project-app:c3d4";
	private const string AppFailed = "The project's image could not be built. The log says why.";
	private const string OtherFailed = "The other project's image could not be built.";
	private const string OtherProjectBuilds = "Another project's image is building.";

	private static readonly string[] AppLog = ["#5 [2/2] RUN make", "ERROR: failed to solve: make: exit code 2"];
	private static readonly string[] OtherLog = ["#7 [2/2] RUN npm ci", "ERROR: failed to solve: npm ci: exit code 1"];

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Render_WhileAndAfterAnotherProjectsBuildFails_KeepsThisProjectsBuiltWithItsTag(bool byPipeline)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Success(AppTag);
		await editor.BuildAsync();
		await AssertBuiltAsync(editor, AppTag);
		string otherProjectFolder = editor.NewOtherProjectFolder();
		editor.Sandboxes.Result = ServiceResult<string>.Failure(OtherFailed);
		editor.Sandboxes.LogLines = OtherLog;
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		Task begun = editor.Sandboxes.NextStarted;
		Task otherBuild = StartOtherProjectsBuild(editor, otherProjectFolder, byPipeline);
		try
		{
			await begun.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			GateRenderedElement build = await editor.BuildButtonAsync();
			Assert.False(build.IsClickable, "Builds run one at a time, so another project's build holds this one back.");
			Assert.False(SandboxEditorHost.IsLoading(build), "The build that runs is not this project's, so Build does not say it is building.");
			Assert.Equal(OtherProjectBuilds, await editor.BuildTitleAsync());
			Assert.Null(await editor.CancelButtonAsync());
			await AssertBuiltAsync(editor, AppTag);
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(otherBuild);
		}

		Assert.Equal(SandboxImageBuildState.Failed, editor.ImageBuilds.Latest(SandboxNames.ProjectKey(otherProjectFolder))?.State);
		await AssertBuiltAsync(editor, AppTag);
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Once the other project's build ended, this project can build.");
		Assert.Null(await editor.BuildTitleAsync());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Render_AfterAnotherProjectsBuildSucceeds_KeepsThisProjectsFailureWithTheEndOfItsLog(bool byPipeline)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(AppFailed);
		editor.Sandboxes.LogLines = AppLog;
		await editor.BuildAsync();
		await AssertFailedAsync(editor, AppFailed, AppLog);
		string otherProjectFolder = editor.NewOtherProjectFolder();
		editor.Sandboxes.Result = ServiceResult<string>.Success(OtherTag);
		editor.Sandboxes.LogLines = null;

		await SandboxEditorHost.WaitForAsync(StartOtherProjectsBuild(editor, otherProjectFolder, byPipeline));

		Assert.Equal(SandboxImageBuildState.Succeeded, editor.ImageBuilds.Latest(SandboxNames.ProjectKey(otherProjectFolder))?.State);
		await AssertFailedAsync(editor, AppFailed, AppLog);
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Once the other project's build ended, this project can build again.");
		Assert.Null(await editor.BuildTitleAsync());
	}

	[Fact]
	public async Task SwitchProject_ToAnotherProjectAndBack_ShowsEachProjectsOwnLastBuild()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		Project app = new() { Id = 1, Name = "app", Folder = editor.ProjectFolder, IsOpen = true };
		Project other = new() { Id = 2, Name = "other", Folder = editor.NewOtherProjectFolder(), IsOpen = true };
		editor.Sandboxes.Result = ServiceResult<string>.Success(AppTag);
		await editor.BuildAsync();
		await AssertBuiltAsync(editor, AppTag);

		await editor.SwitchProjectAsync(other);

		await editor.AssertNoOutcomeAsync();
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Nothing builds, so the other project can build.");
		Assert.Null(await editor.BuildTitleAsync());

		editor.Sandboxes.Result = ServiceResult<string>.Failure(OtherFailed);
		editor.Sandboxes.LogLines = OtherLog;
		await editor.BuildAsync();

		Assert.Equal([SandboxNames.ProjectKey(app.Folder), SandboxNames.ProjectKey(other.Folder)], editor.Sandboxes.Requests.Select(request => request.ProjectKey));
		await AssertFailedAsync(editor, OtherFailed, OtherLog);

		await editor.SwitchProjectAsync(app);

		await AssertBuiltAsync(editor, AppTag);

		await editor.SwitchProjectAsync(other);

		await AssertFailedAsync(editor, OtherFailed, OtherLog);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Cancel_OfARebuild_ShowsNoOutcomeWhileItRunsAndThenTheBuildBeforeAgain(bool previousFailed)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		if (previousFailed)
		{
			editor.Sandboxes.Result = ServiceResult<string>.Failure(AppFailed);
			editor.Sandboxes.LogLines = AppLog;
		}
		else
		{
			editor.Sandboxes.Result = ServiceResult<string>.Success(AppTag);
		}

		await editor.BuildAsync();
		await AssertPreviousBuildAsync(editor, previousFailed);
		editor.Sandboxes.Result = ServiceResult<string>.Success(RebuildTag);
		editor.Sandboxes.LogLines = null;
		editor.Sandboxes.Hold = new TaskCompletionSource().Task;
		Task begun = editor.Sandboxes.NextStarted;
		await editor.StartBuildAsync();
		await begun.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

		GateRenderedElement building = await editor.BuildButtonAsync("Building");
		Assert.True(SandboxEditorHost.IsLoading(building), "The rebuild runs, so Build says it is building.");
		Assert.False(building.IsClickable, "A second build cannot start while one runs.");
		Assert.True((await editor.CancelButtonAsync())?.IsClickable, "A person's build that runs can be cancelled.");
		await editor.AssertNoOutcomeAsync();

		await editor.CancelAsync();

		await editor.Sandboxes.Cancelled.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		GateRenderedElement build = await editor.BuildButtonAsync();
		Assert.True(build.IsClickable, "A cancelled build can be started again.");
		Assert.False(SandboxEditorHost.IsLoading(build), "Nothing builds any more.");
		Assert.Null(await editor.CancelButtonAsync());
		await AssertPreviousBuildAsync(editor, previousFailed);
	}

	// Starts another project's build of the Dockerfile this project has saved too, as a person or the pipeline does, so only the project tells the two builds apart. The task completes when the build ended.
	private static Task StartOtherProjectsBuild(SandboxEditorHost editor, string otherProjectFolder, bool byPipeline)
	{
		if (byPipeline)
		{
			return editor.PipelineBuildAsync(otherProjectFolder);
		}

		Assert.True(editor.ImageBuilds.TryStart(otherProjectFolder, SandboxDefaults.Dockerfile), "Nothing builds, so the other project's build starts.");

		return editor.ImageBuilds.Running;
	}

	// Checks that the page shows a successful build, Built with the tag of the image it made, and nothing of a failure.
	private static async Task AssertBuiltAsync(SandboxEditorHost editor, string tag)
	{
		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal(tag, await editor.BuiltTagAsync());
		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
	}

	// Checks that the page shows a failed build, its error and the end of its log, and nothing of a success.
	private static async Task AssertFailedAsync(SandboxEditorHost editor, string error, string[] log)
	{
		Assert.Equal([error], await editor.Host.AlertsAsync());
		Assert.Equal(string.Join('\n', log), await editor.BuildLogAsync());
		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
	}

	// Checks that the page shows this project's first build: its failure, or Built with its tag.
	private static Task AssertPreviousBuildAsync(SandboxEditorHost editor, bool failed)
	{
		return failed ? AssertFailedAsync(editor, AppFailed, AppLog) : AssertBuiltAsync(editor, AppTag);
	}
}
