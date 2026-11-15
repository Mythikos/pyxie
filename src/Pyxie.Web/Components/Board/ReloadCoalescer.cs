namespace Pyxie.Web.Components.Board;

/// <summary>Reloads data on request without letting reloads pile up. Requests are served one at a time, in the order they were made. A request waits for the reload in flight and then reloads only if no reload that started after the request was made has finished, so a burst of requests costs the reload in flight plus one more. Every caller returns only once data at least as new as its request has been loaded, so a caller that changed something can rely on the result.</summary>
public sealed class ReloadCoalescer
{
	private readonly object _sync = new();
	private readonly Func<CancellationToken, Task> _reload;
	private Task _tail = Task.CompletedTask;
	private int _requests;
	private int _covered;

	/// <summary>Initializes a new instance of the <see cref="ReloadCoalescer"/> class.</summary>
	/// <param name="reload">Loads the data. It never runs twice at once.</param>
	public ReloadCoalescer(Func<CancellationToken, Task> reload)
	{
		_reload = reload;
	}

	/// <summary>Makes sure data loaded after this call is available when the returned task completes.</summary>
	/// <param name="cancellationToken">A token to cancel the wait and the reload.</param>
	/// <returns>A task that completes when such a reload has finished, or faults with the failure of the reload this request ran.</returns>
	public async Task ReloadAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task previous;
		int request;

		lock (_sync)
		{
			request = ++_requests;
			previous = _tail;
			_tail = finished.Task;
		}

		try
		{
			// The earlier request signals completion whether it succeeded, failed, or was cancelled; its own caller observes how it ended.
			await previous;
			cancellationToken.ThrowIfCancellationRequested();

			if (_covered >= request)
			{
				return;
			}

			// Everything requested up to now is covered by the load about to start: its change was made before it asked.
			int covers;

			lock (_sync)
			{
				covers = _requests;
			}

			await _reload(cancellationToken);
			_covered = covers;
		}
		finally
		{
			finished.SetResult();
		}
	}
}
