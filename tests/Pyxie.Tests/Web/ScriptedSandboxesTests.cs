using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The scripted engine the settings page tests build and look up images with keeps its images as the real one does: a build that succeeded leaves its image and any other end leaves the images as they were, an image is found by its project and Dockerfile whatever the line endings, and a lookup answers with what the engine had when it was asked however late the answer arrives.</summary>
public sealed class ScriptedSandboxesTests
{
	private const string Key = "pyxie-app";
	private const string OtherKey = "pyxie-other";
	private const string Dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN make\n";
	private const string OldTag = "pyxie-project-app:old";
	private const string NewTag = "pyxie-project-app:new";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task EnsureImageAsync_WhenTheBuildSucceeds_LeavesTheImageOfItsProjectAndDockerfileUnderTheResultsTagWhateverTheLineEndings()
	{
		ScriptedSandboxes sandboxes = new() { Result = ServiceResult<string>.Success(NewTag) };

		await sandboxes.EnsureImageAsync(Request(Key, "ARG X\r\nFROM Y\r\n"), Token);

		Assert.Equal(NewTag, await sandboxes.FindImageAsync(Key, "ARG X\nFROM Y\n", Token));
		Assert.Null(await sandboxes.FindImageAsync(OtherKey, "ARG X\nFROM Y\n", Token));
		Assert.Null(await sandboxes.FindImageAsync(Key, "ARG X\nFROM Z\n", Token));
	}

	[Fact]
	public async Task EnsureImageAsync_WhenTheBuildSucceedsAgain_ReplacesTheTagOfTheImage()
	{
		ScriptedSandboxes sandboxes = new() { Result = ServiceResult<string>.Success(NewTag) };
		sandboxes.AddImage(Key, Dockerfile, OldTag);

		await sandboxes.EnsureImageAsync(Request(Key, Dockerfile), Token);

		Assert.Equal(NewTag, await sandboxes.FindImageAsync(Key, Dockerfile, Token));
	}

	[Fact]
	public async Task EnsureImageAsync_WhenTheBuildFails_LeavesTheImagesAsTheyWere()
	{
		ScriptedSandboxes sandboxes = new() { Result = ServiceResult<string>.Failure("The project's image could not be built.") };
		sandboxes.AddImage(Key, Dockerfile, OldTag);

		ServiceResult<string> result = await sandboxes.EnsureImageAsync(Request(Key, Dockerfile), Token);

		Assert.False(result.Succeeded, "The stand-in failed the build.");
		Assert.Equal(OldTag, await sandboxes.FindImageAsync(Key, Dockerfile, Token));
		Assert.Single(sandboxes.Images);
	}

	[Fact]
	public async Task EnsureImageAsync_WhenTheBuildIsCancelled_LeavesTheImagesAsTheyWere()
	{
		ScriptedSandboxes sandboxes = new() { Hold = new TaskCompletionSource().Task };
		sandboxes.AddImage(Key, Dockerfile, OldTag);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
		Task<ServiceResult<string>> build = sandboxes.EnsureImageAsync(Request(Key, Dockerfile), cancel.Token);
		await sandboxes.Started.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
		Assert.Equal(OldTag, await sandboxes.FindImageAsync(Key, Dockerfile, Token));
		Assert.Single(sandboxes.Images);
	}

	[Fact]
	public async Task EnsureImageAsync_WhenDockerDoesNotAnswer_LeavesTheImagesAsTheyWere()
	{
		ScriptedSandboxes sandboxes = new() { UnavailableMessage = "Docker is not running." };
		sandboxes.AddImage(Key, Dockerfile, OldTag);

		await Assert.ThrowsAsync<SandboxUnavailableException>(() => sandboxes.EnsureImageAsync(Request(Key, Dockerfile), Token));

		Assert.Equal(OldTag, await sandboxes.FindImageAsync(Key, Dockerfile, Token));
		Assert.Single(sandboxes.Images);
	}

	[Fact]
	public async Task FindImageAsync_AfterAnImageWasAddedAndRemoved_FindsItOnlyWhileTheEngineHasIt()
	{
		ScriptedSandboxes sandboxes = new();
		Assert.Null(await sandboxes.FindImageAsync(Key, Dockerfile, Token));

		sandboxes.AddImage(Key, Dockerfile, OldTag);

		Assert.Equal(OldTag, await sandboxes.FindImageAsync(Key, Dockerfile, Token));
		Assert.Null(await sandboxes.FindImageAsync(OtherKey, Dockerfile, Token));
		Assert.True(sandboxes.RemoveImage(Key, Dockerfile), "The engine had the image.");
		Assert.Null(await sandboxes.FindImageAsync(Key, Dockerfile, Token));
		Assert.False(sandboxes.RemoveImage(Key, Dockerfile), "The engine no longer has the image.");
	}

	[Fact]
	public async Task FindImageAsync_Always_RecordsTheProjectAndTheDockerfileAsAskedInOrder()
	{
		ScriptedSandboxes sandboxes = new();
		sandboxes.AddImage(Key, "ARG X\nFROM Y\n", OldTag);

		await sandboxes.FindImageAsync(Key, "ARG X\r\nFROM Y\r\n", Token);
		await sandboxes.FindImageAsync(OtherKey, Dockerfile, Token);

		IReadOnlyList<(string ProjectKey, string Dockerfile)> lookups = sandboxes.Lookups;
		Assert.Equal(2, lookups.Count);
		Assert.Equal((Key, "ARG X\r\nFROM Y\r\n"), lookups[0]);
		Assert.Equal((OtherKey, Dockerfile), lookups[1]);
	}

	[Fact]
	public async Task FindImageAsync_WhileHeld_AnswersWhenReleasedWithWhatTheEngineHadWhenAskedAndLaterLookupsAnswerAtOnce()
	{
		ScriptedSandboxes sandboxes = new();
		sandboxes.AddImage(Key, Dockerfile, OldTag);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		sandboxes.LookupHold = release.Task;
		Task<string?> held = sandboxes.FindImageAsync(Key, Dockerfile, Token);
		await sandboxes.LookupStarted.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		sandboxes.RemoveImage(Key, Dockerfile);
		sandboxes.LookupHold = null;

		Assert.Null(await sandboxes.FindImageAsync(Key, Dockerfile, Token));
		Assert.False(held.IsCompleted, "The lookup that was asked while the hold was set stays held.");

		release.SetResult();

		Assert.Equal(OldTag, await held.WaitAsync(RuntimeTestTimeouts.Wait, Token));
		Assert.Equal(2, sandboxes.Lookups.Count);
	}

	[Fact]
	public async Task FindImageAsync_WhileHeldAndCancelled_EndsCancelledAndTellsTheTest()
	{
		ScriptedSandboxes sandboxes = new() { LookupHold = new TaskCompletionSource().Task };
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
		Task<string?> held = sandboxes.FindImageAsync(Key, Dockerfile, cancel.Token);
		await sandboxes.LookupStarted.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		Assert.False(sandboxes.LookupCancelled.IsCompleted, "Nothing cancelled a lookup yet.");

		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held);
		await sandboxes.LookupCancelled.WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	[Fact]
	public async Task NextLookupStarted_Always_CompletesWhenTheNextLookupIsAskedAndNotBefore()
	{
		ScriptedSandboxes sandboxes = new();
		await sandboxes.FindImageAsync(Key, Dockerfile, Token);
		Task next = sandboxes.NextLookupStarted;
		Assert.False(next.IsCompleted, "The lookup before it does not count.");

		await sandboxes.FindImageAsync(Key, Dockerfile, Token);

		await next.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		Assert.False(sandboxes.NextLookupStarted.IsCompleted, "A lookup that is yet to be asked is not started.");
	}

	[Fact]
	public async Task FindImageAsync_WhenDockerDoesNotAnswer_ThrowsAfterRecordingTheLookupWhileBuildsStillWork()
	{
		ScriptedSandboxes sandboxes = new() { LookupUnavailableMessage = "Docker is not running." };
		sandboxes.AddImage(Key, Dockerfile, OldTag);

		SandboxUnavailableException unavailable = await Assert.ThrowsAsync<SandboxUnavailableException>(() => sandboxes.FindImageAsync(Key, Dockerfile, Token));

		Assert.Equal("Docker is not running.", unavailable.Message);
		Assert.Single(sandboxes.Lookups);
		ServiceResult<string> built = await sandboxes.EnsureImageAsync(Request(Key, Dockerfile), Token);
		Assert.True(built.Succeeded, "Only finding an image was made unavailable.");
	}

	[Fact]
	public async Task FindImageAsync_WhenOnlyBuildsDoNotAnswer_StillFindsTheImage()
	{
		ScriptedSandboxes sandboxes = new() { UnavailableMessage = "Docker is not running." };
		sandboxes.AddImage(Key, Dockerfile, OldTag);

		Assert.Equal(OldTag, await sandboxes.FindImageAsync(Key, Dockerfile, Token));
	}

	private static SandboxImageRequest Request(string projectKey, string dockerfile)
	{
		return new SandboxImageRequest(projectKey, dockerfile, Rebuild: true, Path.Combine(Path.GetTempPath(), "pyxie-scripted-sandboxes-unused.log"));
	}
}
