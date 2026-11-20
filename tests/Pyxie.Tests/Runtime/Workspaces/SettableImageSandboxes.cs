using System.Collections.Concurrent;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Sandboxes that pass everything to another <see cref="ISandboxes"/> except the image, whose answer the test chooses: every image request gets it, and finding the image answers with its tag when it is one and with nothing when it is a failure. Every image request is remembered.</summary>
public sealed class SettableImageSandboxes : ISandboxes
{
	private readonly ISandboxes _inner;
	private readonly ServiceResult<string> _image;
	private readonly ConcurrentQueue<SandboxImageRequest> _requests = new();

	/// <summary>Initializes a new instance of the <see cref="SettableImageSandboxes"/> class.</summary>
	/// <param name="inner">The sandboxes everything else goes to.</param>
	/// <param name="image">What every image request gets.</param>
	public SettableImageSandboxes(ISandboxes inner, ServiceResult<string> image)
	{
		_inner = inner;
		_image = image;
	}

	/// <summary>Gets the image requests, in order.</summary>
	public IReadOnlyList<SandboxImageRequest> Requests => [.. _requests];

	/// <inheritdoc/>
	public Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		return _inner.CheckAvailableAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		_requests.Enqueue(request);

		return Task.FromResult(_image);
	}

	/// <inheritdoc/>
	public Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return Task.FromResult(_image.Succeeded ? _image.Value : null);
	}

	/// <inheritdoc/>
	public Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return _inner.RemoveUnusedImagesAsync(projectKey, dockerfile, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		return _inner.StartAsync(spec, cancellationToken);
	}

	/// <inheritdoc/>
	public Task QuiesceCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		return _inner.QuiesceCardAsync(projectKey, cardId, cancellationToken);
	}

	/// <inheritdoc/>
	public Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		return _inner.RemoveCardAsync(projectKey, cardId, cancellationToken);
	}

	/// <inheritdoc/>
	public Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken)
	{
		return _inner.RemoveLeftoverRoundsAsync(projectKey, cancellationToken);
	}

	/// <inheritdoc/>
	public Task RemoveProjectAsync(string projectKey, CancellationToken cancellationToken)
	{
		return _inner.RemoveProjectAsync(projectKey, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		return _inner.ListCardsAsync(projectKey, cancellationToken);
	}
}
