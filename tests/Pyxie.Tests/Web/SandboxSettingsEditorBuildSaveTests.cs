using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A build in the background does not hold the Dockerfile: it can be saved while a build runs. The build's outcome describes the Dockerfile it built, so once a different Dockerfile is saved the editor shows nothing about it when it ends.</summary>
public sealed class SandboxSettingsEditorBuildSaveTests
{
	private const string Typed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make cmake\n";
	private const string BuildFailed = "The project's image could not be built. The log says why.";

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Save_WhileABuildRuns_StoresTheDockerfileAndShowsNoOutcomeWhenTheBuildEndsBecauseItDescribesTheOldOne(bool byPipeline, bool fails)
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

		Task build;
		if (byPipeline)
		{
			build = editor.PipelineBuildAsync();
		}
		else
		{
			await editor.StartBuildAsync();
			build = editor.ImageBuilds.Running;
		}

		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
			await editor.TypeDockerfileAsync(Typed);
			Assert.True((await editor.SaveButtonAsync()).IsClickable, "A build in the background does not hold the Dockerfile, so a change typed meanwhile can be saved.");

			await editor.SaveAsync();

			Assert.Equal(Typed, (await ReadAsync(database)).Dockerfile);
			Assert.Empty(await editor.Host.AlertsAsync());
			Assert.False((await editor.SaveButtonAsync()).IsClickable, "Once saved, there is nothing to save until the Dockerfile changes again.");
			Assert.True(SandboxEditorHost.IsLoading(await editor.BuildButtonAsync("Building")), "Saving does not stop the build that runs.");
			if (byPipeline)
			{
				Assert.Null(await editor.CancelButtonAsync());
			}
			else
			{
				Assert.True((await editor.CancelButtonAsync())?.IsClickable, "Saving does not take away the person's chance to cancel their build.");
			}
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(build);
		}

		await editor.AssertNoOutcomeAsync();
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "The saved Dockerfile was not the one built, so it can be built.");
		Assert.Null(await editor.CancelButtonAsync());
	}

	private static Task<SandboxSettings> ReadAsync(TestDatabase database)
	{
		return new SandboxSettingsService(database.ContextFactory, database.Clock).GetAsync(TestContext.Current.CancellationToken);
	}
}
