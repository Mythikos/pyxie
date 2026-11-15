using System.Collections.Concurrent;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A stand-in for the sandboxes the settings page builds a project's image with. A build writes the log lines the test scripted to the request's log file and then returns the result the test scripted, or throws <see cref="SandboxUnavailableException"/> as Docker not answering does; it can also be held until the test releases it. Every request is recorded. The page never starts or removes a sandbox, so those members fail the test that reaches them.</summary>
internal sealed class ScriptedSandboxes : ISandboxes
{
	private readonly ConcurrentQueue<SandboxImageRequest> _requests = new();
	private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Gets or sets what a build returns; a built image's tag unless the test scripts a failure.</summary>
	public ServiceResult<string> Result { get; set; } = ServiceResult<string>.Success("pyxie-project-test");

	/// <summary>Gets or sets the lines a build writes to its log file, or <see langword="null"/> to write no log at all.</summary>
	public IReadOnlyList<string>? LogLines { get; set; }

	/// <summary>Gets or sets the message of the <see cref="SandboxUnavailableException"/> a build throws, or <see langword="null"/> when Docker answers.</summary>
	public string? UnavailableMessage { get; set; }

	/// <summary>Gets or sets a task a build waits for before it ends, so a test can look at the page while the build runs; <see langword="null"/> to end at once.</summary>
	public Task? Hold { get; set; }

	/// <summary>Gets a task that completes once the first build has started.</summary>
	public Task Started => _started.Task;

	/// <summary>Gets every build request, in order.</summary>
	public IReadOnlyList<SandboxImageRequest> Requests => [.. _requests];

	/// <inheritdoc/>
	public Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Success());
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		_requests.Enqueue(request);
		_started.TrySetResult();

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

		return Result;
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
}
