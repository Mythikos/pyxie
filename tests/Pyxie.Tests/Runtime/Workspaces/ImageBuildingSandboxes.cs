using System.Collections.Concurrent;
using System.Threading.Channels;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A stand-in for the way <see cref="DockerSandboxes"/> builds images, in the engine's own order: a request whose image is there answers at once, any other waits its turn (builds run one at a time), looks again, calls <see cref="SandboxImageRequest.BuildStarting"/> once it holds the turn and has to build, empties the log file, and then ends as the test scripted: the <see cref="Result"/>, a failure, <see cref="SandboxUnavailableException"/>, or <see cref="OperationCanceledException"/> when its token is cancelled, which the engine honours while it waits and while it builds. A test sees where each request stands (<see cref="QueuedAsync"/>, <see cref="BeganAsync"/>), can <see cref="OccupyAsync"/> the engine as a build ahead, and can hold builds until it finishes them (<see cref="Holding"/>). Finding the image answers from <see cref="ImagePresent"/>, whatever the project and Dockerfile, and records what it was asked (<see cref="Lookups"/>). The image builds never start a sandbox or remove anything, so those members fail the test that reaches them.</summary>
public sealed class ImageBuildingSandboxes : ISandboxes
{
	/// <summary>The tag of the image a build makes, and of an image that is there.</summary>
	public const string Tag = "pyxie-project-stand-in";

	private readonly SemaphoreSlim _engine = new(1, 1);
	private readonly SemaphoreSlim _finishes = new(0);
	private readonly Channel<SandboxImageRequest> _queued = Channel.CreateUnbounded<SandboxImageRequest>();
	private readonly Channel<SandboxImageRequest> _began = Channel.CreateUnbounded<SandboxImageRequest>();
	private readonly ConcurrentQueue<SandboxImageRequest> _requests = new();
	private readonly ConcurrentQueue<(string ProjectKey, string Dockerfile)> _lookups = new();
	private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private volatile bool _imagePresent;
	private volatile bool _holding;
	private volatile IReadOnlyList<string>? _logLines;
	private volatile string? _unavailableWhenAsked;
	private volatile string? _unavailableWhileBuilding;
	private ServiceResult<string> _result = ServiceResult<string>.Success(Tag);

	/// <summary>Gets or sets whether the image is there, which a request that is not a rebuild finds both before it waits for its turn and once it has it, and which finding the image reports; a rebuild always builds.</summary>
	public bool ImagePresent
	{
		get => _imagePresent;
		set => _imagePresent = value;
	}

	/// <summary>Gets or sets whether a build that began waits to be finished (<see cref="Finish"/>) before it ends, so a test can look at the builds while one runs. One call to <see cref="Finish"/> ends one build.</summary>
	public bool Holding
	{
		get => _holding;
		set => _holding = value;
	}

	/// <summary>Gets or sets what a build that ends normally returns: the image's <see cref="Tag"/> unless the test scripts a failure.</summary>
	public ServiceResult<string> Result
	{
		get => Volatile.Read(ref _result);
		set => Volatile.Write(ref _result, value);
	}

	/// <summary>Gets or sets the lines a build writes to its log file after it emptied it, or <see langword="null"/> to leave it empty.</summary>
	public IReadOnlyList<string>? LogLines
	{
		get => _logLines;
		set => _logLines = value;
	}

	/// <summary>Gets or sets the message of the <see cref="SandboxUnavailableException"/> a request or finding the image throws as soon as it is asked, before it waits for anything, as an engine that is not running does; <see langword="null"/> while the engine answers.</summary>
	public string? UnavailableWhenAsked
	{
		get => _unavailableWhenAsked;
		set => _unavailableWhenAsked = value;
	}

	/// <summary>Gets or sets the message of the <see cref="SandboxUnavailableException"/> a build throws once it began, as an engine that stops answering does; <see langword="null"/> while the engine answers.</summary>
	public string? UnavailableWhileBuilding
	{
		get => _unavailableWhileBuilding;
		set => _unavailableWhileBuilding = value;
	}

	/// <summary>Gets a task that completes once the token of any request was cancelled, which is how the engine is told to stop it.</summary>
	public Task Cancelled => _cancelled.Task;

	/// <summary>Gets every request, in the order they arrived.</summary>
	public IReadOnlyList<SandboxImageRequest> Requests => [.. _requests];

	/// <summary>Gets the project key and the Dockerfile of every time the image was looked for, in order, also when the engine did not answer.</summary>
	public IReadOnlyList<(string ProjectKey, string Dockerfile)> Lookups => [.. _lookups];

	/// <summary>Takes the engine for the test, as a build ahead of everything else does, so a request that has to build waits for <see cref="Vacate"/>.</summary>
	/// <param name="cancellationToken">A token to cancel the wait.</param>
	/// <returns>A task that completes once the test has the engine.</returns>
	public Task OccupyAsync(CancellationToken cancellationToken)
	{
		return _engine.WaitAsync(cancellationToken);
	}

	/// <summary>Gives the engine back after <see cref="OccupyAsync"/>, which lets the request that waited longest begin.</summary>
	public void Vacate()
	{
		_engine.Release();
	}

	/// <summary>Ends one build that is held (see <see cref="Holding"/>), or the next one to begin.</summary>
	public void Finish()
	{
		_finishes.Release();
	}

	/// <summary>Waits for the next request to be in line for the engine, which is as soon as it found the image is not there (or is a rebuild); it may already hold the engine. The requests arrive in the order of their turns.</summary>
	/// <param name="cancellationToken">A token to cancel the wait.</param>
	/// <returns>The request.</returns>
	public async Task<SandboxImageRequest> QueuedAsync(CancellationToken cancellationToken)
	{
		return await _queued.Reader.ReadAsync(cancellationToken);
	}

	/// <summary>Waits for the next build to begin, which is the moment it called <see cref="SandboxImageRequest.BuildStarting"/> and emptied its log.</summary>
	/// <param name="cancellationToken">A token to cancel the wait.</param>
	/// <returns>The request whose build began.</returns>
	public async Task<SandboxImageRequest> BeganAsync(CancellationToken cancellationToken)
	{
		return await _began.Reader.ReadAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Success());
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		_requests.Enqueue(request);
		using CancellationTokenRegistration seesCancellation = cancellationToken.Register(() => _cancelled.TrySetResult());

		if (_unavailableWhenAsked is string unavailable)
		{
			throw new SandboxUnavailableException(unavailable);
		}

		if (!request.Rebuild && _imagePresent)
		{
			return ServiceResult<string>.Success(Tag);
		}

		// The wait is started before the request is announced, so the order of the announcements is the order of the turns.
		Task turn = _engine.WaitAsync(cancellationToken);
		_queued.Writer.TryWrite(request);
		await turn;
		try
		{
			// Another request may have built it while this one waited.
			if (!request.Rebuild && _imagePresent)
			{
				return ServiceResult<string>.Success(Tag);
			}

			request.BuildStarting?.Invoke();
			Directory.CreateDirectory(Path.GetDirectoryName(request.LogPath) ?? throw new InvalidOperationException($"The log path {request.LogPath} has no folder."));
			await File.WriteAllLinesAsync(request.LogPath, _logLines ?? [], CancellationToken.None);
			_began.Writer.TryWrite(request);

			if (_holding)
			{
				await _finishes.WaitAsync(cancellationToken);
			}

			if (_unavailableWhileBuilding is string stopped)
			{
				throw new SandboxUnavailableException(stopped);
			}

			return Result;
		}
		finally
		{
			_engine.Release();
		}
	}

	/// <inheritdoc/>
	public Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		_lookups.Enqueue((projectKey, dockerfile));

		if (_unavailableWhenAsked is string unavailable)
		{
			return Task.FromException<string?>(new SandboxUnavailableException(unavailable));
		}

		return Task.FromResult(_imagePresent ? Tag : null);
	}

	/// <inheritdoc/>
	public Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Image builds never remove unused images.");
	}

	/// <inheritdoc/>
	public Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Image builds never start a sandbox.");
	}

	/// <inheritdoc/>
	public Task QuiesceCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Image builds never remove a card's sandboxes.");
	}

	/// <inheritdoc/>
	public Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Image builds never remove leftover sandboxes.");
	}

	/// <inheritdoc/>
	public Task RemoveProjectAsync(string projectKey, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Image builds never remove a project.");
	}

	/// <inheritdoc/>
	public Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Image builds never list cards' sandboxes.");
	}
}
