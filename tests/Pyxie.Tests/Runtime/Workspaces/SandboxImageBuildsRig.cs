using System.Collections.Concurrent;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary><see cref="SandboxImageBuilds"/> over <see cref="ImageBuildingSandboxes"/>, which builds in the engine's own order, a clock the test controls, and a project folder of its own (whose <c>runs</c> folder holds the build log). It records what <see cref="SandboxImageBuilds.Building"/>, the project's <see cref="SandboxImageBuilds.Latest"/> build, and <see cref="SandboxImageBuilds.PersonBuild"/> were each time the builds announced a change, so a test sees the sequence and not only where it ended. A test that needs a second project asks for its folder (<see cref="CreateOtherProject"/>), which the rig deletes with its own. Every wait is bounded by <see cref="RuntimeTestTimeouts.Wait"/>, so a build that never ends fails the test instead of stalling the run.</summary>
public sealed class SandboxImageBuildsRig : IAsyncDisposable
{
	/// <summary>The Dockerfile every request carries.</summary>
	public const string Dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo tools\n";

	private readonly ConcurrentQueue<(SandboxImageBuild? Building, SandboxImageBuild? Latest, SandboxImageBuild? PersonBuild)> _snapshots = new();
	private readonly List<string> _otherProjectFolders = [];
	private bool _buildsDisposed;

	/// <summary>Initializes a new instance of the <see cref="SandboxImageBuildsRig"/> class: builds the services and creates the project folder. Nothing is built until a test asks.</summary>
	public SandboxImageBuildsRig()
	{
		ProjectFolder = TempFolder.Create("pyxie-ib");
		ProjectKey = SandboxNames.ProjectKey(ProjectFolder);
		LogPath = WorkspaceLayout.ImageLogPath(ProjectFolder);
		Engine = new ImageBuildingSandboxes();
		Builds = new SandboxImageBuilds(Engine, Clock);
		Builds.Changed += RecordSnapshot;
	}

	/// <summary>Gets the clock the builds are timed by, which stays where the test leaves it.</summary>
	public TestClock Clock { get; } = new();

	/// <summary>Gets the engine stand-in the builds are made by.</summary>
	public ImageBuildingSandboxes Engine { get; }

	/// <summary>Gets the builds under test.</summary>
	public SandboxImageBuilds Builds { get; }

	/// <summary>Gets the project's folder.</summary>
	public string ProjectFolder { get; }

	/// <summary>Gets the project's key, as the builds work it out from <see cref="ProjectFolder"/>.</summary>
	public string ProjectKey { get; }

	/// <summary>Gets the file every build of the project writes its output to.</summary>
	public string LogPath { get; }

	/// <summary>Gets what <see cref="SandboxImageBuilds.Building"/>, <see cref="SandboxImageBuilds.Latest"/> of <see cref="ProjectKey"/>, and <see cref="SandboxImageBuilds.PersonBuild"/> were at each announced change, in order.</summary>
	public IReadOnlyList<(SandboxImageBuild? Building, SandboxImageBuild? Latest, SandboxImageBuild? PersonBuild)> Snapshots => [.. _snapshots];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Gets the build of the project that is shown now: <see cref="SandboxImageBuilds.Latest"/> of <see cref="ProjectKey"/>.</summary>
	public SandboxImageBuild? Latest => Builds.Latest(ProjectKey);

	/// <summary>Gets the build <see cref="SandboxImageBuilds.Latest"/> shows for another project.</summary>
	/// <param name="projectFolder">The other project's folder, from <see cref="CreateOtherProject"/>.</param>
	/// <returns>The build.</returns>
	public SandboxImageBuild? LatestOf(string projectFolder)
	{
		return Builds.Latest(SandboxNames.ProjectKey(projectFolder));
	}

	/// <summary>Creates the folder of another project, which the builds tell from the project's own and which <see cref="DisposeAsync"/> deletes.</summary>
	/// <returns>The folder.</returns>
	public string CreateOtherProject()
	{
		string folder = TempFolder.Create("pyxie-ib-other");
		_otherProjectFolders.Add(folder);

		return folder;
	}

	/// <summary>Starts a person's build of the project's image.</summary>
	/// <returns><see langword="true"/> when it started.</returns>
	public bool TryStart()
	{
		return Builds.TryStart(ProjectFolder, Dockerfile);
	}

	/// <summary>Starts a person's build of another project's image.</summary>
	/// <param name="projectFolder">The other project's folder, from <see cref="CreateOtherProject"/>.</param>
	/// <returns><see langword="true"/> when it started.</returns>
	public bool TryStart(string projectFolder)
	{
		return Builds.TryStart(projectFolder, Dockerfile);
	}

	/// <summary>Asks for the project's image as a round does and returns at once, with the task of the answer.</summary>
	/// <param name="cancellationToken">The token of the round.</param>
	/// <returns>The task of the round's wait for its image.</returns>
	public Task<ServiceResult<string>> EnsureAsync(CancellationToken cancellationToken)
	{
		return Builds.EnsureAsync(ProjectFolder, Dockerfile, cancellationToken);
	}

	/// <summary>Asks for another project's image as a round does and returns at once, with the task of the answer.</summary>
	/// <param name="projectFolder">The other project's folder, from <see cref="CreateOtherProject"/>.</param>
	/// <param name="cancellationToken">The token of the round.</param>
	/// <returns>The task of the round's wait for its image.</returns>
	public Task<ServiceResult<string>> EnsureAsync(string projectFolder, CancellationToken cancellationToken)
	{
		return Builds.EnsureAsync(projectFolder, Dockerfile, cancellationToken);
	}

	/// <summary>Waits for the next request to be in line for the engine.</summary>
	/// <returns>The request.</returns>
	public async Task<SandboxImageRequest> QueuedAsync()
	{
		return await Engine.QueuedAsync(Token).WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	/// <summary>Waits for the next build to begin.</summary>
	/// <returns>The request whose build began.</returns>
	public async Task<SandboxImageRequest> BeganAsync()
	{
		return await Engine.BeganAsync(Token).WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	/// <summary>Waits until the token of a request was cancelled, which is how the engine is told to stop a build.</summary>
	/// <returns>A task that completes when it was.</returns>
	public async Task CancelledAsync()
	{
		await Engine.Cancelled.WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	/// <summary>Waits until the person's build ended, however it ended.</summary>
	/// <returns>A task that completes when it did.</returns>
	public async Task EndedAsync()
	{
		await Builds.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	/// <summary>Waits for any task, such as a round's wait for its image, without ever waiting longer than <see cref="RuntimeTestTimeouts.Wait"/>.</summary>
	/// <typeparam name="TResult">The task's result.</typeparam>
	/// <param name="task">The task.</param>
	/// <returns>Its result.</returns>
	public static async Task<TResult> WithinAsync<TResult>(Task<TResult> task)
	{
		return await task.WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	/// <summary>Waits for any task without ever waiting longer than <see cref="RuntimeTestTimeouts.Wait"/>.</summary>
	/// <param name="task">The task.</param>
	/// <returns>A task that completes when it did.</returns>
	public static async Task WithinAsync(Task task)
	{
		await task.WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	/// <summary>Disposes the builds, which cancels a person's build that runs, once; <see cref="DisposeAsync"/> does not do it again.</summary>
	public void DisposeBuilds()
	{
		if (_buildsDisposed)
		{
			return;
		}

		_buildsDisposed = true;
		Builds.Dispose();
	}

	/// <summary>Disposes the builds and deletes the project folder, and the folders of the other projects.</summary>
	/// <returns>A task that completes when the folders are gone.</returns>
	public async ValueTask DisposeAsync()
	{
		DisposeBuilds();
		await TempFolder.DeleteAsync(ProjectFolder);

		foreach (string folder in _otherProjectFolders)
		{
			await TempFolder.DeleteAsync(folder);
		}
	}

	private void RecordSnapshot()
	{
		_snapshots.Enqueue((Builds.Building, Builds.Latest(ProjectKey), Builds.PersonBuild));
	}
}
