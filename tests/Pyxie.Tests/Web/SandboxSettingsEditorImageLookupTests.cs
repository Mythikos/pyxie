using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The settings page asks the engine for the project's image of the saved Dockerfile, so Built shows that image however and whenever it was built, before Pyxie started included, and stops showing it once anything removed it. The page asks when it opens, after a save, and after a build ends; only the answer to its latest question counts, an engine that does not answer shows no image and no error, a failed rebuild shows next to the image it left, and a page that is closed stops waiting for an answer.</summary>
public sealed class SandboxSettingsEditorImageLookupTests
{
	private const string Saved = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make\n";
	private const string Typed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make cmake\n";
	private const string SavedTag = "pyxie-project-app:a1f0";
	private const string TypedTag = "pyxie-project-app:b2e1";
	private const string RebuildTag = "pyxie-project-app:c3d4";
	private const string OtherTag = "pyxie-project-other:d5e6";
	private const string BuildFailed = "The project's image could not be built. The log says why.";
	private const string Unavailable = "Docker is not running. Start Docker Desktop and try again.";

	private static readonly string[] Log = ["#5 [2/2] RUN make", "ERROR: failed to solve: make: exit code 2"];

	// The key of a project the test never opens, whose image the engine can have all the same.
	private static readonly string OtherKey = SandboxNames.ProjectKey(Path.Combine(Path.GetTempPath(), "pyxie-sbx-never-opened"));

	[Fact]
	public async Task Render_WhenTheEngineAlreadyHasTheImageOfTheSavedDockerfile_ShowsBuiltWithItsTagWithoutBuilding()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);

		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));

		await AssertBuiltAsync(editor, SavedTag);
		Assert.Empty(editor.Sandboxes.Requests);
		Assert.Equal([(editor.ProjectKey, Saved)], editor.Sandboxes.Lookups);
	}

	[Fact]
	public async Task Render_BeforeThePageIsInteractive_DoesNotAskTheEngine()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		ScriptedSandboxes sandboxes = new();

		string html = await SandboxEditorHost.RenderHtmlAsync(database, sandboxes: sandboxes);

		Assert.Contains("RUN apt-get install -y make", html, StringComparison.Ordinal);
		Assert.Empty(sandboxes.Lookups);
	}

	[Fact]
	public async Task Render_WhenTheEngineHasNoImage_AsksForTheImageOfTheSavedDockerfileAndShowsNoOutcome()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);

		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		await editor.AssertNoOutcomeAsync();
		Assert.Empty(editor.Sandboxes.Requests);
		Assert.Equal([(editor.ProjectKey, Saved)], editor.Sandboxes.Lookups);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Render_WhenTheEngineHasTheImageOfAnotherProjectOrAnotherDockerfile_ShowsNoBuiltUntilItHasThisOne(bool anotherProject)
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) =>
		{
			if (anotherProject)
			{
				sandboxes.AddImage(OtherKey, Saved, OtherTag);
			}
			else
			{
				sandboxes.AddImage(SandboxNames.ProjectKey(folder), Typed, TypedTag);
			}
		});

		await editor.AssertNoOutcomeAsync();
		Assert.Equal([(editor.ProjectKey, Saved)], editor.Sandboxes.Lookups);

		editor.Sandboxes.AddImage(editor.ProjectKey, Saved, SavedTag);
		await editor.ReopenAsync();

		await AssertBuiltAsync(editor, SavedTag);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Save_OfTheBuiltDockerfileAgain_ShowsBuiltOnlyWhileTheEngineStillHasItsImage(bool removed)
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Success(SavedTag);
		await editor.BuildAsync();
		await AssertBuiltAsync(editor, SavedTag);

		await editor.TypeDockerfileAsync(Typed);
		await editor.SaveAsync();

		await editor.AssertNoOutcomeAsync();
		Assert.Equal((editor.ProjectKey, Typed), editor.Sandboxes.Lookups[^1]);
		if (removed)
		{
			Assert.True(editor.Sandboxes.RemoveImage(editor.ProjectKey, Saved), "The engine kept the image that was built until the pipeline's cleanup removed it.");
		}

		await editor.TypeDockerfileAsync(Saved);
		await editor.SaveAsync();

		Assert.Equal((editor.ProjectKey, Saved), editor.Sandboxes.Lookups[^1]);
		if (removed)
		{
			await editor.AssertNoOutcomeAsync();
		}
		else
		{
			await AssertBuiltAsync(editor, SavedTag);
		}

		Assert.Single(editor.Sandboxes.Requests);
	}

	[Fact]
	public async Task Build_ThatFailsWhileTheImageIsThere_ShowsOnlyBuildingWhileItRunsAndThenTheFailureNextToBuilt()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));
		await AssertBuiltAsync(editor, SavedTag);
		editor.Sandboxes.Result = ServiceResult<string>.Failure(BuildFailed);
		editor.Sandboxes.LogLines = Log;
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		editor.Sandboxes.Hold = release.Task;

		await editor.StartBuildAsync();
		try
		{
			await editor.Sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);

			Assert.True(SandboxEditorHost.IsLoading(await editor.BuildButtonAsync("Building")), "The rebuild runs, so Build says it is building.");
			await editor.AssertNoOutcomeAsync();
		}
		finally
		{
			release.TrySetResult();
			await SandboxEditorHost.WaitForAsync(editor.ImageBuilds.Running);
		}

		Assert.Equal([BuildFailed], await editor.Host.AlertsAsync());
		Assert.Equal(string.Join('\n', Log), await editor.BuildLogAsync());
		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal(SavedTag, await editor.BuiltTagAsync());
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "Once the rebuild ended, it can be tried again.");
	}

	[Fact]
	public async Task Render_WhenTheEngineDoesNotAnswerTheLookup_ShowsNoBuiltAndNoErrorAndBuildCanStart()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) =>
		{
			sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag);
			sandboxes.LookupUnavailableMessage = Unavailable;
		});

		await editor.AssertNoOutcomeAsync();
		Assert.Equal([(editor.ProjectKey, Saved)], editor.Sandboxes.Lookups);
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "An engine that did not answer can still be asked to build, which says what is wrong.");
		Assert.Null(await editor.BuildTitleAsync());

		editor.Sandboxes.LookupUnavailableMessage = null;
		await editor.ReopenAsync();

		await AssertBuiltAsync(editor, SavedTag);
	}

	[Fact]
	public async Task Save_WhenTheEngineDoesNotAnswerTheLookup_SavesAndShowsNoBuiltAndNoError()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) =>
		{
			sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag);
			sandboxes.AddImage(SandboxNames.ProjectKey(folder), Typed, TypedTag);
		});
		await AssertBuiltAsync(editor, SavedTag);
		editor.Sandboxes.LookupUnavailableMessage = Unavailable;

		await editor.TypeDockerfileAsync(Typed);
		await editor.SaveAsync();

		await editor.AssertNoOutcomeAsync();
		Assert.Equal((editor.ProjectKey, Typed), editor.Sandboxes.Lookups[^1]);
		Assert.False((await editor.SaveButtonAsync()).IsClickable, "The Dockerfile was saved, so there is nothing left to save.");
		Assert.True((await editor.BuildButtonAsync()).IsClickable, "An engine that did not answer can still be asked to build, which says what is wrong.");
	}

	[Fact]
	public async Task Render_WhenTheEngineStopsAnsweringAndAnotherProjectsBuildEnds_NoLongerShowsBuilt()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));
		await AssertBuiltAsync(editor, SavedTag);
		editor.Sandboxes.LookupUnavailableMessage = Unavailable;

		await BuildAnotherProjectAsync(editor);

		await editor.AssertNoOutcomeAsync();
		Assert.True(editor.Sandboxes.Lookups.Count > 1, "The other project's build made the page ask the engine again.");
	}

	[Fact]
	public async Task Reopen_WhileTheEngineIsSlowToAnswer_ShowsNoBuiltUntilTheAnswerArrivesAndThenBuilt()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task reopen = await ReopenWithLookupHeldAsync(editor, release.Task);
		try
		{
			await editor.AssertNoOutcomeAsync();
			Assert.False(reopen.IsCompleted, "The page waits for the engine's answer.");
		}
		finally
		{
			release.TrySetResult();
			await reopen.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		}

		await AssertBuiltAsync(editor, SavedTag);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Save_WhileTheLookupOfTheDockerfileBeforeIsHeld_KeepsTheAnswerForTheSavedDockerfileWhenTheOldAnswerArrives(bool newImagePresent)
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));
		if (newImagePresent)
		{
			editor.Sandboxes.AddImage(editor.ProjectKey, Typed, TypedTag);
		}

		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task reopen = await ReopenWithLookupHeldAsync(editor, release.Task);
		try
		{
			await editor.TypeDockerfileAsync(Typed);
			await editor.SaveAsync();

			Assert.False(reopen.IsCompleted, "The lookup of the Dockerfile the page opened with has not answered yet.");
			Assert.Equal([(editor.ProjectKey, Saved), (editor.ProjectKey, Saved), (editor.ProjectKey, Typed)], editor.Sandboxes.Lookups);
			await AssertNewAnswerAsync(editor, newImagePresent);
		}
		finally
		{
			release.TrySetResult();
			await reopen.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		}

		await AssertNewAnswerAsync(editor, newImagePresent);
		Assert.Equal(3, editor.Sandboxes.Lookups.Count);
	}

	[Fact]
	public async Task Build_WhenItEnds_AsksTheEngineAgainAndShowsTheTagItHasNow()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));
		await AssertBuiltAsync(editor, SavedTag);
		editor.Sandboxes.Result = ServiceResult<string>.Success(RebuildTag);

		await editor.BuildAsync();

		await AssertBuiltAsync(editor, RebuildTag);
		Assert.True(editor.Sandboxes.Lookups.Count > 1, "The page asked the engine again once the build ended.");
		Assert.All(editor.Sandboxes.Lookups, lookup => Assert.Equal((editor.ProjectKey, Saved), lookup));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Render_WhenAnotherProjectsBuildOfTheSameDockerfileEnds_AsksOnlyForThisProjectsImageAndShowsNoBuilt(bool byPipeline)
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		editor.Sandboxes.Result = ServiceResult<string>.Success(OtherTag);

		string otherProjectFolder = await BuildAnotherProjectAsync(editor, byPipeline);

		await editor.AssertNoOutcomeAsync();
		Assert.Equal(OtherTag, editor.Sandboxes.Images.GetValueOrDefault((SandboxNames.ProjectKey(otherProjectFolder), Saved)));
		Assert.True(editor.Sandboxes.Lookups.Count > 1, "The other project's build made the page ask the engine again.");
		Assert.All(editor.Sandboxes.Lookups, lookup => Assert.Equal((editor.ProjectKey, Saved), lookup));
	}

	[Fact]
	public async Task Render_WhenTheImageWasRemovedAndAnotherProjectsBuildEnds_NoLongerShowsBuilt()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) => sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag));
		await AssertBuiltAsync(editor, SavedTag);
		Assert.True(editor.Sandboxes.RemoveImage(editor.ProjectKey, Saved), "The engine had the image until the pipeline's cleanup removed it.");

		await BuildAnotherProjectAsync(editor);

		await editor.AssertNoOutcomeAsync();
	}

	[Fact]
	public async Task Render_WithUnsavedChangesWhenABuildEnds_AsksForTheImageOfTheSavedDockerfile()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database, arrange: (sandboxes, folder) =>
		{
			sandboxes.AddImage(SandboxNames.ProjectKey(folder), Saved, SavedTag);
			sandboxes.AddImage(SandboxNames.ProjectKey(folder), Typed, TypedTag);
		});
		await editor.TypeDockerfileAsync(Typed);

		await BuildAnotherProjectAsync(editor);

		await AssertBuiltAsync(editor, SavedTag);
		Assert.True(editor.Sandboxes.Lookups.Count > 1, "The other project's build made the page ask the engine again.");
		Assert.All(editor.Sandboxes.Lookups, lookup => Assert.Equal((editor.ProjectKey, Saved), lookup));
	}

	[Fact]
	public async Task Close_WhileTheLookupIsHeld_CancelsItAndClosesWithoutError()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		Task reopen = await ReopenWithLookupHeldAsync(editor, new TaskCompletionSource().Task);
		Assert.False(editor.Sandboxes.LookupCancelled.IsCompleted, "Nothing cancelled the lookup yet.");

		await editor.Host.CloseAsync();

		await editor.Sandboxes.LookupCancelled.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		await reopen.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		Assert.Equal(2, editor.Sandboxes.Lookups.Count);
	}

	// Leaves the page and comes back while the engine is slow: the new page's lookup of the Dockerfile it opened with waits for the hold, and every lookup asked after it answers at once. The task returned completes once that lookup answered and the page has used or ignored its answer.
	private static async Task<Task> ReopenWithLookupHeldAsync(SandboxEditorHost editor, Task hold)
	{
		Task asked = editor.Sandboxes.NextLookupStarted;
		editor.Sandboxes.LookupHold = hold;
		Task reopen = editor.ReopenAsync();
		await asked.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
		editor.Sandboxes.LookupHold = null;

		return reopen;
	}

	// Runs another project's build of the Dockerfile this project saved, as the pipeline or a person does, and waits until it ended; the page hears of the end before the test next reads it. Returns the other project's folder.
	private static async Task<string> BuildAnotherProjectAsync(SandboxEditorHost editor, bool byPipeline = true)
	{
		string otherProjectFolder = editor.NewOtherProjectFolder();
		Task build;
		if (byPipeline)
		{
			build = editor.PipelineBuildAsync(otherProjectFolder, Saved);
		}
		else
		{
			Assert.True(editor.ImageBuilds.TryStart(otherProjectFolder, Saved), "Nothing builds, so the other project's build starts.");
			build = editor.ImageBuilds.Running;
		}

		await SandboxEditorHost.WaitForAsync(build);

		return otherProjectFolder;
	}

	// Checks that the page shows the engine's answer for the Dockerfile saved last: Built with that image's tag when the engine has it, and nothing otherwise.
	private static Task AssertNewAnswerAsync(SandboxEditorHost editor, bool newImagePresent)
	{
		return newImagePresent ? AssertBuiltAsync(editor, TypedTag) : editor.AssertNoOutcomeAsync();
	}

	// Checks that the page shows Built with the tag of the image and nothing of a failure.
	private static async Task AssertBuiltAsync(SandboxEditorHost editor, string tag)
	{
		Assert.Equal("Built", await editor.BuiltStatusAsync());
		Assert.Equal(tag, await editor.BuiltTagAsync());
		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Null(await editor.BuildLogAsync());
	}

	// Saves the project's Dockerfile before the page opens, as another tab would.
	private static async Task SaveElsewhereAsync(TestDatabase database, string dockerfile)
	{
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(dockerfile), 0, TestContext.Current.CancellationToken));
	}
}
