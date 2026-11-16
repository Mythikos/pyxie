using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Builds run one at a time, so another project's build, a person's or the pipeline's, holds Build back while it runs and says why. It is not this project's build, so the label stays Build, Cancel is not offered, and nothing about it is shown once it ends.</summary>
public sealed class SandboxSettingsEditorOtherProjectBuildTests
{
	private const string Typed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make cmake\n";
	private const string BuildFailed = "The project's image could not be built. The log says why.";
	private const string SaveFirst = "Save the Dockerfile first.";
	private const string OtherProjectBuilds = "Another project's image is building.";

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Render_WhileAnotherProjectsImageBuilds_DisablesBuildSaysWhyAndOffersNoCancel(bool byPipeline)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		Task otherBuild = StartOtherProjectsBuild(editor, byPipeline);
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			GateRenderedElement build = await editor.BuildButtonAsync();
			Assert.False(build.IsClickable, "Builds run one at a time, so another project's build holds this one back.");
			Assert.False(SandboxEditorHost.IsLoading(build), "The build that runs is not this project's, so Build does not say it is building.");
			Assert.Equal(OtherProjectBuilds, await editor.BuildTitleAsync());
			Assert.Null(await editor.CancelButtonAsync());
			await editor.AssertNoOutcomeAsync();
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(otherBuild);
		}

		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Once the other project's build ended, this project can build.");
		Assert.Null(await editor.BuildTitleAsync());
	}

	[Fact]
	public async Task Render_WithUnsavedChangesWhileAnotherProjectsImageBuilds_SaysToSaveFirstAndKeepsSaveEnabled()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;
		Task otherBuild = StartOtherProjectsBuild(editor, byPipeline: false);
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			await editor.TypeDockerfileAsync(Typed);

			Assert.Equal(SaveFirst, await editor.BuildTitleAsync());
			Assert.True((await editor.SaveButtonAsync()).IsClickable, "Another project's build does not hold this project's Dockerfile.");
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(otherBuild);
		}
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Render_WhenAnotherProjectsBuildEnded_ShowsNoOutcomeOfItAndBuildCanStart(bool byPipeline, bool fails)
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		if (fails)
		{
			editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
			editor.Sandboxes.LogLines = ["ERROR: failed to solve"];
		}

		await SandboxEditorHost.WaitForAsync(StartOtherProjectsBuild(editor, byPipeline));

		await editor.AssertNoOutcomeAsync();
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The other project's build ended, so this project can build.");
		Assert.Null(await editor.BuildTitleAsync());
		Assert.Null(await editor.CancelButtonAsync());
	}

	private static Task StartOtherProjectsBuild(SandboxEditorHost editor, bool byPipeline)
	{
		string otherProjectFolder = editor.NewOtherProjectFolder();
		if (byPipeline)
		{
			return editor.PipelineBuildAsync(otherProjectFolder);
		}

		Assert.True(editor.ImageBuilds.TryStart(otherProjectFolder, SandboxDefaults.Dockerfile), "Nothing builds, so the other project's build starts.");

		return editor.ImageBuilds.Running;
	}
}
