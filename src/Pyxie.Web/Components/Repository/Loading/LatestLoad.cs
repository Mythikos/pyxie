using Pyxie.Runtime.Git;

namespace Pyxie.Web.Components.Repository.Loading;

/// <summary>Runs a component's loads so that only the newest one counts. A component that reloads when its parameters change can have several loads in flight, and a slow old one must not overwrite what a newer one showed. Starting a load cancels the previous one (which kills its git processes), and a load that was replaced applies nothing and reports nothing. Dispose it with the component.</summary>
public sealed class LatestLoad : IDisposable
{
	private CancellationTokenSource? _current;
	private bool _disposed;

	/// <summary>Runs a load, replacing the one in flight.</summary>
	/// <typeparam name="TResult">What the load produces.</typeparam>
	/// <param name="load">Reads the data. It receives the token that is cancelled when another load starts or the component is disposed.</param>
	/// <param name="apply">Takes the data. It runs only when the load completed and was not replaced.</param>
	/// <param name="fail">Takes git's message when the load threw a <see cref="GitException"/>, for example because the repository's folder was moved. It runs only when the load was not replaced. Without it the exception reaches the caller, which is for a load that handles git's failures itself.</param>
	/// <returns>A task that completes when the load was applied, replaced, or reported its failure.</returns>
	public async Task RunAsync<TResult>(Func<CancellationToken, Task<TResult>> load, Action<TResult> apply, Action<string>? fail = null)
	{
		CancellationToken token = Begin();

		try
		{
			TResult result = await load(token);

			if (!token.IsCancellationRequested)
			{
				apply(result);
			}
		}
		catch (GitException exception) when (fail is not null)
		{
			if (!token.IsCancellationRequested)
			{
				fail(exception.Message);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_disposed = true;
		Cancel();
	}

	private CancellationToken Begin()
	{
		Cancel();

		if (_disposed)
		{
			return new CancellationToken(canceled: true);
		}

		_current = new CancellationTokenSource();

		return _current.Token;
	}

	private void Cancel()
	{
		_current?.Cancel();
		_current?.Dispose();
		_current = null;
	}
}
