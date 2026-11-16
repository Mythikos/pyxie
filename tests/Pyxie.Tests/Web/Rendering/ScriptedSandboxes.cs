using System.Collections.Concurrent;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A stand-in for the sandboxes the settings page builds a project's image with and asks for it. A build reports itself through the request's <see cref="SandboxImageRequest.BuildStarting"/> as soon as it begins, writes the log lines the test scripted to the request's log file, and then returns the result the test scripted, or throws <see cref="SandboxUnavailableException"/> as Docker not answering does; it can also be held until the test releases it, and a rebuild can be made to wait behind a build ahead of it. A build that is cancelled while it waits or is held ends with <see cref="OperationCanceledException"/>, and <see cref="Cancelled"/> tells the test its token was cancelled. Every request is recorded. The engine's images are scripted too: a build whose result succeeded leaves the image of its project and Dockerfile, with the result's tag, and a build that failed, was cancelled, or found Docker not answering leaves the images as they were, as the real engine does. The test can add an image as if it was built before Pyxie started (<see cref="AddImage"/>) and remove one as the pipeline's cleanup or a person does (<see cref="RemoveImage"/>). Finding an image answers from them, whatever the line endings of the Dockerfile, and is recorded (<see cref="Lookups"/>); what it answers is decided when it is asked, so a lookup that is held (<see cref="LookupHold"/>) answers late, as a slow engine does, with what the engine had when it was asked. It throws <see cref="SandboxUnavailableException"/> while <see cref="LookupUnavailableMessage"/> is set. The page never starts or removes a sandbox, so those members fail the test that reaches them.</summary>
internal sealed class ScriptedSandboxes : ISandboxes
{
	private readonly ConcurrentQueue<SandboxImageRequest> _requests = new();
	private readonly ConcurrentDictionary<(string ProjectKey, string Dockerfile), string> _images = new();
	private readonly ConcurrentQueue<(string ProjectKey, string Dockerfile)> _lookups = new();
	private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _lookupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _lookupCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private TaskCompletionSource _nextStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private TaskCompletionSource _nextLookupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private volatile string? _lookupUnavailableMessage;
	private volatile Task? _lookupHold;

	/// <summary>Gets or sets what a build returns; a built image's tag unless the test scripts a failure.</summary>
	public ServiceResult<string> Result { get; set; } = ServiceResult<string>.Success("pyxie-project-test");

	/// <summary>Gets or sets the lines a build writes to its log file, or <see langword="null"/> to write no log at all.</summary>
	public IReadOnlyList<string>? LogLines { get; set; }

	/// <summary>Gets or sets the message of the <see cref="SandboxUnavailableException"/> a build throws, or <see langword="null"/> when Docker answers.</summary>
	public string? UnavailableMessage { get; set; }

	/// <summary>Gets or sets a task a build waits for before it ends, so a test can look at the page while the build runs; <see langword="null"/> to end at once.</summary>
	public Task? Hold { get; set; }

	/// <summary>Gets or sets a task a rebuild waits for before it begins, as it waits for the build ahead of it; a round's build, which is never a rebuild, does not wait. <see langword="null"/> to begin at once.</summary>
	public Task? Ahead { get; set; }

	/// <summary>Gets or sets the message of the <see cref="SandboxUnavailableException"/> finding an image throws, as Docker not answering does, which is separate from <see cref="UnavailableMessage"/> of a build; <see langword="null"/> when Docker answers. A lookup reads it when it is asked.</summary>
	public string? LookupUnavailableMessage
	{
		get => _lookupUnavailableMessage;
		set => _lookupUnavailableMessage = value;
	}

	/// <summary>Gets or sets a task finding an image waits for before it answers, which also holds one that throws, so a test can look at the page while the engine is slow; <see langword="null"/> to answer at once. A lookup reads it when it is asked, so setting it to <see langword="null"/> again lets the lookups after that answer at once while the ones held stay held. A held lookup ends with <see cref="OperationCanceledException"/> when its token is cancelled.</summary>
	public Task? LookupHold
	{
		get => _lookupHold;
		set => _lookupHold = value;
	}

	/// <summary>Gets a task that completes once the first build has started.</summary>
	public Task Started => _started.Task;

	/// <summary>Gets a task that completes the next time a build starts, which a test reads before it asks for a build when an earlier one already started.</summary>
	public Task NextStarted => Volatile.Read(ref _nextStarted).Task;

	/// <summary>Gets a task that completes once the token of any build was cancelled, which is how the engine is told to stop it.</summary>
	public Task Cancelled => _cancelled.Task;

	/// <summary>Gets a task that completes once the first lookup of an image has been asked, whether or not it is held.</summary>
	public Task LookupStarted => _lookupStarted.Task;

	/// <summary>Gets a task that completes the next time a lookup of an image is asked, which a test reads before it does what makes the page ask when an earlier one already was.</summary>
	public Task NextLookupStarted => Volatile.Read(ref _nextLookupStarted).Task;

	/// <summary>Gets a task that completes once a lookup ended because its token was cancelled while it was held, which is how the engine is told to stop it.</summary>
	public Task LookupCancelled => _lookupCancelled.Task;

	/// <summary>Gets every build request, in order.</summary>
	public IReadOnlyList<SandboxImageRequest> Requests => [.. _requests];

	/// <summary>Gets every lookup of an image, in the order they were asked: the project's key and the Dockerfile, as the page gave it.</summary>
	public IReadOnlyList<(string ProjectKey, string Dockerfile)> Lookups => [.. _lookups];

	/// <summary>Gets the images the engine has now, each with its tag, by the project's key and the Dockerfile with its line endings made <c>\n</c>.</summary>
	public IReadOnlyDictionary<(string ProjectKey, string Dockerfile), string> Images => new Dictionary<(string ProjectKey, string Dockerfile), string>(_images);

	/// <summary>Adds an image to the engine, as one that was built before Pyxie started: nothing built it in this test.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The Dockerfile it was built from.</param>
	/// <param name="tag">The image's tag.</param>
	public void AddImage(string projectKey, string dockerfile, string tag)
	{
		_images[(projectKey, Normalized(dockerfile))] = tag;
	}

	/// <summary>Removes an image from the engine, as the pipeline's cleanup or a person does.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The Dockerfile it was built from.</param>
	/// <returns><see langword="true"/> when the engine had it.</returns>
	public bool RemoveImage(string projectKey, string dockerfile)
	{
		return _images.TryRemove((projectKey, Normalized(dockerfile)), out _);
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

		if (request.Rebuild && Ahead is Task ahead)
		{
			await ahead.WaitAsync(cancellationToken);
		}

		request.BuildStarting?.Invoke();
		_started.TrySetResult();
		Interlocked.Exchange(ref _nextStarted, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

		if (Hold is Task hold)
		{
			await hold.WaitAsync(cancellationToken);
		}

		if (UnavailableMessage is string message)
		{
			throw new SandboxUnavailableException(message);
		}

		if (LogLines is IReadOnlyList<string> lines)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(request.LogPath) ?? throw new InvalidOperationException($"The log path {request.LogPath} has no folder."));
			await File.WriteAllLinesAsync(request.LogPath, lines, cancellationToken);
		}

		ServiceResult<string> result = Result;
		if (result.Succeeded && result.Value is string tag)
		{
			_images[(request.ProjectKey, Normalized(request.Dockerfile))] = tag;
		}

		return result;
	}

	/// <inheritdoc/>
	public async Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		// What the lookup answers is decided now, before it is recorded and announced, so a test that sees it and then changes the engine never changes this lookup's answer; only when the answer arrives depends on the hold.
		Task? hold = _lookupHold;
		string? unavailable = _lookupUnavailableMessage;
		string? tag = _images.GetValueOrDefault((projectKey, Normalized(dockerfile)));
		_lookups.Enqueue((projectKey, dockerfile));
		_lookupStarted.TrySetResult();
		Interlocked.Exchange(ref _nextLookupStarted, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

		try
		{
			if (hold is not null)
			{
				await hold.WaitAsync(cancellationToken);
			}

			cancellationToken.ThrowIfCancellationRequested();
		}
		catch (OperationCanceledException)
		{
			_lookupCancelled.TrySetResult();
			throw;
		}

		return unavailable is null ? tag : throw new SandboxUnavailableException(unavailable);
	}

	/// <inheritdoc/>
	public Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("The settings page never removes unused images.");
	}

	/// <inheritdoc/>
	public Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("The settings page never starts a sandbox.");
	}

	/// <inheritdoc/>
	public Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("The settings page never removes a card's sandboxes.");
	}

	/// <inheritdoc/>
	public Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("The settings page never removes leftover sandboxes.");
	}

	/// <inheritdoc/>
	public Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("The settings page never lists cards' sandboxes.");
	}

	// The engine tags an image by its Dockerfile with the line endings it builds, so two spellings of the same lines are one image.
	private static string Normalized(string dockerfile)
	{
		return dockerfile.ReplaceLineEndings("\n");
	}
}
