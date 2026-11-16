using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>The runtime of <see cref="RuntimeHarness"/> with a project open whose Dockerfile was saved, and with the stand-in sandboxes building images the way the container engine does (<see cref="ImageBuildingSandboxes"/>), so a test makes a person's build succeed, fail, or be cancelled and sees when the pipeline asks the sandboxes to remove what builds left (<see cref="Removals"/>). The image is there for rounds, so only a person's rebuild builds. The pipeline is left stopped and the orchestrator is not started until the test asks.</summary>
public sealed class ImageCleanupRig : IAsyncDisposable
{
	private const string SavedDockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo saved\n";

	private readonly TempGitRepository _source;

	private ImageCleanupRig(TempGitRepository source, RuntimeHarness harness, string dockerfile)
	{
		_source = source;
		Harness = harness;
		Dockerfile = dockerfile;
		ProjectKey = SandboxNames.ProjectKey(harness.ProjectFolder);
		Builds = harness.Services.GetRequiredService<SandboxImageBuilds>();
		Engine = new ImageBuildingSandboxes { ImagePresent = true };
		harness.Sandboxes.EnsureImage = Engine.EnsureImageAsync;
	}

	/// <summary>Gets the runtime, with the open project.</summary>
	public RuntimeHarness Harness { get; }

	/// <summary>Gets the engine stand-in that builds every image the runtime asks for.</summary>
	public ImageBuildingSandboxes Engine { get; }

	/// <summary>Gets the runtime's builds of images, the same ones the orchestrator watches.</summary>
	public SandboxImageBuilds Builds { get; }

	/// <summary>Gets the project's key.</summary>
	public string ProjectKey { get; }

	/// <summary>Gets the project's saved Dockerfile, which is not the default one.</summary>
	public string Dockerfile { get; }

	/// <summary>Gets every request to remove unused images the sandboxes got, in order.</summary>
	public IReadOnlyList<(string ProjectKey, string Dockerfile)> Removals => Harness.Sandboxes.UnusedImageRemovals;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Imports a repository as the open project and saves a Dockerfile of its own.</summary>
	/// <returns>The rig. The caller disposes it, which also deletes the repository.</returns>
	public static async Task<ImageCleanupRig> CreateAsync()
	{
		TempGitRepository source = await TempGitRepository.CreateAsync();
		RuntimeHarness harness = new(roundTimeoutMinutes: 1);

		try
		{
			await harness.ConnectRepositoryAsync(source);
			await using AsyncServiceScope scope = harness.CreateScope();
			string dockerfile = ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ISandboxSettingsService>().SaveAsync(new SandboxSettingsInput(SavedDockerfile), 0, Token)).Dockerfile;

			return new ImageCleanupRig(source, harness, dockerfile);
		}
		catch
		{
			await harness.DisposeAsync();
			await source.DisposeAsync();

			throw;
		}
	}

	/// <summary>Has a person build the project's image and waits until it ended.</summary>
	/// <param name="outcome">How it ends: a build that succeeds or fails, or one that is held until a person cancels it.</param>
	/// <returns>A task that completes when the build ended. The test fails when it did not end as asked.</returns>
	public async Task BuildAsync(SandboxImageBuildState outcome)
	{
		SandboxImageBuild? before = Builds.Latest(ProjectKey);
		Engine.Holding = outcome == SandboxImageBuildState.Cancelled;
		Engine.Result = outcome == SandboxImageBuildState.Failed ? ServiceResult<string>.Failure("The project's sandbox image could not be built.") : ServiceResult<string>.Success(ImageBuildingSandboxes.Tag);
		Assert.True(Builds.TryStart(Harness.ProjectFolder, Dockerfile), "A person's build can be asked for while none runs.");

		if (outcome == SandboxImageBuildState.Cancelled)
		{
			await Engine.BeganAsync(Token).WaitAsync(RuntimeTestTimeouts.Wait, Token);
			Assert.True(Builds.Cancel(), "The held build runs, so it can be cancelled.");
		}

		await Builds.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		Assert.Null(Builds.PersonBuild);

		if (outcome == SandboxImageBuildState.Cancelled)
		{
			// A cancelled build leaves the image, and so the project's last result, as it was.
			Assert.Same(before, Builds.Latest(ProjectKey));
		}
		else
		{
			Assert.Equal(outcome, Builds.Latest(ProjectKey)?.State);
		}
	}

	/// <summary>Waits until the sandboxes got a number of requests to remove unused images.</summary>
	/// <param name="count">How many.</param>
	/// <returns>A task that completes when they did. The test fails when they did not in time.</returns>
	public async Task WaitForRemovalsAsync(int count)
	{
		await Harness.WaitUntilAsync(() => Task.FromResult(Removals.Count >= count), $"the sandboxes to be asked {count} time(s) to remove unused images", RuntimeTestTimeouts.Wait);
	}

	/// <summary>Asserts that every request to remove unused images so far was for this project and its saved Dockerfile, and that there were this many.</summary>
	/// <param name="count">How many.</param>
	public void AssertRemovals(int count)
	{
		Assert.Equal(Enumerable.Repeat((ProjectKey, Dockerfile), count), Removals);
	}

	/// <summary>Watches the pipeline for a while, for something that should not happen, which has nothing to wait for.</summary>
	/// <returns>A task that completes when the pipeline was watched long enough that an orchestrator that wrongly did it would have.</returns>
	public async Task WatchAsync()
	{
		await Task.Delay(RuntimeTestTimeouts.IdleObservation, Token);
	}

	/// <summary>Stops the pipeline and the orchestrator, deletes the runtime's folders, and then the imported repository.</summary>
	/// <returns>A task that completes when everything is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		try
		{
			await Harness.DisposeAsync();
		}
		finally
		{
			await _source.DisposeAsync();
		}
	}
}
