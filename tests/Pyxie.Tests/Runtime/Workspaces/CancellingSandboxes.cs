using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Sandboxes that pass everything to another <see cref="ISandboxes"/> and, once armed, cancel a token as soon as the next sandbox has started: the pipeline being stopped while a round's sandbox is being prepared, at a point the test chooses rather than after a delay.</summary>
public sealed class CancellingSandboxes : ISandboxes
{
	private readonly ISandboxes _inner;
	private CancellationTokenSource? _armed;

	/// <summary>Initializes a new instance of the <see cref="CancellingSandboxes"/> class.</summary>
	/// <param name="inner">The sandboxes everything goes to.</param>
	public CancellingSandboxes(ISandboxes inner)
	{
		_inner = inner;
	}

	/// <summary>Arms the stand-in: the next sandbox that starts cancels the token, once.</summary>
	/// <param name="cancellation">The token source to cancel.</param>
	public void CancelOnNextStart(CancellationTokenSource cancellation)
	{
		_armed = cancellation;
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		return _inner.CheckAvailableAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		return _inner.EnsureImageAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return _inner.FindImageAsync(projectKey, dockerfile, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return _inner.RemoveUnusedImagesAsync(projectKey, dockerfile, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		ISandbox sandbox = await _inner.StartAsync(spec, cancellationToken);
		CancellationTokenSource? armed = Interlocked.Exchange(ref _armed, null);
		if (armed is not null)
		{
			await armed.CancelAsync();
		}

		return sandbox;
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
