using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Runtime.Workspaces;

/// <summary>Owns the builds of projects' sandbox images that people and the pipeline ask for, so everyone sees the same ones (see <c>docs/sandboxing.md</c>). A person's build runs in the background, outlives the page that started it, and can be cancelled; it is refused while any build runs, because builds run one at a time and a second would only wait to build the same image again. The pipeline's builds, for a round whose image is not there yet, are reported as they happen; a round whose image is there reports nothing. Each project keeps the last of its builds that finished until Pyxie stops, whatever other projects build meanwhile. Which image a project has is asked of the engine instead, so it is known however and whenever the image was built.</summary>
public sealed class SandboxImageBuilds : IDisposable
{
	// The end of a failed build's log is where docker says what went wrong.
	private const int LogTailLines = 20;

	private readonly ISandboxes _sandboxes;
	private readonly TimeProvider _timeProvider;
	private readonly CancellationTokenSource _shutdown = new();
	private readonly PipelineSignal _ended = new();
	private readonly Lock _gate = new();
	private readonly Dictionary<string, SandboxImageBuild> _finished = new(StringComparer.Ordinal);
	private SandboxImageBuild? _building;
	private SandboxImageBuild? _personBuild;
	private CancellationTokenSource? _personCancel;
	private Task _running = Task.CompletedTask;
	private int _nextId;
	private int _succeededBuilds;

	/// <summary>Initializes a new instance of the <see cref="SandboxImageBuilds"/> class.</summary>
	/// <param name="sandboxes">The sandboxes, which build the images.</param>
	/// <param name="timeProvider">The clock builds are timed by.</param>
	public SandboxImageBuilds(ISandboxes sandboxes, TimeProvider timeProvider)
	{
		_sandboxes = sandboxes;
		_timeProvider = timeProvider;
	}

	/// <summary>Raised when a build is asked for, begins, or ends. Handlers run on whichever thread made the change and must not throw.</summary>
	public event Action? Changed;

	/// <summary>Gets the build running now, of whichever project, or when none is, a person's build that waits to begin; <see langword="null"/> when there is neither.</summary>
	public SandboxImageBuild? Building
	{
		get
		{
			lock (_gate)
			{
				return _building ?? _personBuild;
			}
		}
	}

	/// <summary>Gets a person's build from the moment they ask for it until it ends, which <see cref="Cancel"/> stops; <see langword="null"/> when there is none. It can wait while the pipeline's build runs, and <see cref="Building"/> shows that one meanwhile.</summary>
	public SandboxImageBuild? PersonBuild
	{
		get
		{
			lock (_gate)
			{
				return _personBuild;
			}
		}
	}

	/// <summary>Gets a value indicating whether a build can be asked for now: none runs and no person's build waits.</summary>
	public bool CanStart
	{
		get
		{
			lock (_gate)
			{
				return _personBuild is null && _building is null;
			}
		}
	}

	/// <summary>Gets the task of a person's build, which completes when it ends however it ends; a completed task when none runs.</summary>
	public Task Running
	{
		get
		{
			lock (_gate)
			{
				return _running;
			}
		}
	}

	/// <summary>Gets how many builds have succeeded since Pyxie started. Each one can leave images and build cache that nothing needs, which the pipeline removes once no round runs.</summary>
	internal int SucceededBuilds
	{
		get
		{
			lock (_gate)
			{
				return _succeededBuilds;
			}
		}
	}

	/// <summary>Gets a project's build that runs now, or when none does, the last of its builds that succeeded or failed; <see langword="null"/> before one. A cancelled build leaves it as it was, because it leaves the image as it was.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <returns>The build.</returns>
	public SandboxImageBuild? Latest(string projectKey)
	{
		lock (_gate)
		{
			// Both are read under one lock, so a rebuild never shows next to the outcome of the build before it.
			if (_building?.ProjectKey == projectKey)
			{
				return _building;
			}

			if (_personBuild?.ProjectKey == projectKey)
			{
				return _personBuild;
			}

			return _finished.GetValueOrDefault(projectKey);
		}
	}

	/// <summary>Finds a project's image for a Dockerfile, which is there once a build of it succeeded, in this run of Pyxie or an earlier one, until something removes it. It is asked of the engine every time.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <param name="dockerfile">The project's Dockerfile.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The image's tag, or <see langword="null"/> when the engine does not have it.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	public Task<string?> FindImageAsync(string projectFolder, string dockerfile, CancellationToken cancellationToken)
	{
		return _sandboxes.FindImageAsync(SandboxNames.ProjectKey(projectFolder), dockerfile, cancellationToken);
	}

	/// <summary>Starts a person's rebuild of a project's image from scratch, which also brings the agents' command lines in it up to date, unless a build is running or waiting.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <param name="dockerfile">The project's saved Dockerfile.</param>
	/// <returns><see langword="true"/> when the build started; <see langword="false"/> when one was already running or waiting.</returns>
	public bool TryStart(string projectFolder, string dockerfile)
	{
		lock (_gate)
		{
			if (_personBuild is not null || _building is not null)
			{
				return false;
			}

			SandboxImageBuild build = NewBuild(projectFolder, dockerfile, SandboxImageBuildRequester.Person);
			CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
			_personBuild = build;
			_building = build;
			_personCancel = cancel;
			_running = Task.Run(() => RunPersonAsync(build, cancel), CancellationToken.None);
		}

		Changed?.Invoke();

		return true;
	}

	/// <summary>Cancels a person's build, which stops it in the engine and leaves the project's image as it was.</summary>
	/// <returns><see langword="true"/> when there was one to cancel.</returns>
	public bool Cancel()
	{
		CancellationTokenSource? cancel;
		lock (_gate)
		{
			cancel = _personCancel;
		}

		if (cancel is null)
		{
			return false;
		}

		try
		{
			cancel.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The build ended meanwhile.
			return false;
		}

		return true;
	}

	/// <summary>Gets a project's image for a round, building it when it is not there yet, and reports the build while it runs.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <param name="dockerfile">The Dockerfile the round read.</param>
	/// <param name="cancellationToken">A token to cancel the build: the pipeline stopping, or Pyxie shutting down.</param>
	/// <returns>The image's tag, or why it could not be built.</returns>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	/// <exception cref="OperationCanceledException">The build was cancelled.</exception>
	public async Task<ServiceResult<string>> EnsureAsync(string projectFolder, string dockerfile, CancellationToken cancellationToken)
	{
		SandboxImageBuild? build = null;
		SandboxImageRequest request = new(SandboxNames.ProjectKey(projectFolder), dockerfile, Rebuild: false, WorkspaceLayout.ImageLogPath(projectFolder))
		{
			BuildStarting = () =>
			{
				build = NewBuild(projectFolder, dockerfile, SandboxImageBuildRequester.Pipeline);
				Begin(build);
			},
		};

		ServiceResult<string> result;
		try
		{
			result = await _sandboxes.EnsureImageAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (build is not null)
		{
			End(build, SandboxImageBuildState.Cancelled, tag: null, error: null);
			throw;
		}
		catch (Exception exception) when (build is not null && exception is not OperationCanceledException)
		{
			End(build, SandboxImageBuildState.Failed, tag: null, exception.Message);
			throw;
		}

		if (build is not null)
		{
			End(build, result.Succeeded ? SandboxImageBuildState.Succeeded : SandboxImageBuildState.Failed, result.Value, result.Error);
		}

		return result;
	}

	/// <summary>Cancels a person's build that is running.</summary>
	public void Dispose()
	{
		_shutdown.Cancel();
		_shutdown.Dispose();
	}

	/// <summary>Gets a task that completes the next time a build ends, however it ends.</summary>
	/// <returns>The task.</returns>
	internal Task NextEndAsync()
	{
		return _ended.NextAsync();
	}

	private static string? Tail(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			string[] lines = File.ReadAllLines(path);

			return lines.Length == 0 ? null : string.Join('\n', lines.Skip(Math.Max(0, lines.Length - LogTailLines)));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private SandboxImageBuild NewBuild(string projectFolder, string dockerfile, SandboxImageBuildRequester requestedBy)
	{
		return new SandboxImageBuild(Interlocked.Increment(ref _nextId), SandboxNames.ProjectKey(projectFolder), dockerfile, requestedBy, SandboxImageBuildState.Running, _timeProvider.GetUtcNow().UtcDateTime, EndedUtc: null, Tag: null, Error: null, WorkspaceLayout.ImageLogPath(projectFolder), LogTail: null);
	}

	// Owns a person's build from start to end: every outcome, an unexpected exception included, is reported, so nothing is left unobserved. Its token source is disposed last; Cancel copes with one disposed meanwhile.
	private async Task RunPersonAsync(SandboxImageBuild build, CancellationTokenSource cancel)
	{
		SandboxImageRequest request = new(build.ProjectKey, build.Dockerfile, Rebuild: true, build.LogPath) { BuildStarting = () => Begin(build) };
		try
		{
			ServiceResult<string> result = await _sandboxes.EnsureImageAsync(request, cancel.Token);
			End(build, result.Succeeded ? SandboxImageBuildState.Succeeded : SandboxImageBuildState.Failed, result.Value, result.Error ?? "The image could not be built.");
		}
		catch (OperationCanceledException)
		{
			End(build, SandboxImageBuildState.Cancelled, tag: null, error: null);
		}
		catch (Exception exception)
		{
			End(build, SandboxImageBuildState.Failed, tag: null, exception.Message);
		}
		finally
		{
			cancel.Dispose();
		}
	}

	// A build begins once the one ahead of it ended, so it is the one running now.
	private void Begin(SandboxImageBuild build)
	{
		lock (_gate)
		{
			_building = build;
		}

		Changed?.Invoke();
	}

	// A person's build that never began (cancelled while the pipeline's ran) ends without hiding the build that is running. A build that succeeded or failed becomes its project's last; a cancelled one left the image as it was, so it leaves the project's last build as it was too.
	private void End(SandboxImageBuild build, SandboxImageBuildState state, string? tag, string? error)
	{
		SandboxImageBuild ended = build with
		{
			State = state,
			EndedUtc = _timeProvider.GetUtcNow().UtcDateTime,
			Tag = state == SandboxImageBuildState.Succeeded ? tag : null,
			Error = state == SandboxImageBuildState.Failed ? error : null,
			LogTail = state == SandboxImageBuildState.Failed ? Tail(build.LogPath) : null,
		};

		lock (_gate)
		{
			if (_building?.Id == build.Id)
			{
				_building = null;
			}

			if (state != SandboxImageBuildState.Cancelled)
			{
				_finished[build.ProjectKey] = ended;
			}

			// A person's build is over in the same step its outcome is recorded, so nobody sees one without the other.
			if (_personBuild?.Id == build.Id)
			{
				_personBuild = null;
				_personCancel = null;
			}

			if (state == SandboxImageBuildState.Succeeded)
			{
				_succeededBuilds++;
			}
		}

		Changed?.Invoke();
		_ended.Pulse();
	}
}
