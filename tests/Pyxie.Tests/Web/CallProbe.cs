using System.Collections.Concurrent;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>An asynchronous call that tests can hold open, fail, and count, so concurrency is decided by the test rather than by timing. Calls are numbered from one in the order they start.</summary>
internal sealed class CallProbe
{
	private static readonly TimeSpan s_giveUp = TimeSpan.FromSeconds(10);

	private readonly ConcurrentDictionary<int, TaskCompletionSource> _started = new();
	private readonly ConcurrentDictionary<int, TaskCompletionSource> _holds = new();
	private readonly ConcurrentDictionary<int, Exception> _failures = new();
	private int _startedCount;
	private int _running;
	private int _mostAtOnce;

	/// <summary>Gets how many calls have started.</summary>
	public int Started => Volatile.Read(ref _startedCount);

	/// <summary>Gets the most calls that were ever running at the same time.</summary>
	public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

	/// <summary>Makes the call with the given one-based number wait until <see cref="Release"/>.</summary>
	/// <param name="number">The one-based number of the call, in the order calls start.</param>
	public void Hold(int number)
	{
		_holds[number] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	/// <summary>Lets a held call finish.</summary>
	/// <param name="number">The one-based number of the call.</param>
	public void Release(int number)
	{
		_holds[number].SetResult();
	}

	/// <summary>Makes the call with the given number throw once it is released.</summary>
	/// <param name="number">The one-based number of the call.</param>
	/// <param name="failure">The exception it throws.</param>
	public void FailWith(int number, Exception failure)
	{
		_failures[number] = failure;
	}

	/// <summary>Waits until the call with the given number has started.</summary>
	/// <param name="number">The one-based number of the call.</param>
	/// <returns>A task that completes when it has started, or fails when it never does.</returns>
	public Task WaitForStartAsync(int number)
	{
		return StartedSignal(number).Task.WaitAsync(s_giveUp, TestContext.Current.CancellationToken);
	}

	/// <summary>Enters the call: it counts, waits while held, and throws when told to fail.</summary>
	/// <param name="cancellationToken">A token to cancel the wait.</param>
	/// <returns>A task that completes when the call may go on.</returns>
	public async Task EnterAsync(CancellationToken cancellationToken)
	{
		int number = Interlocked.Increment(ref _startedCount);
		int running = Interlocked.Increment(ref _running);

		int most;

		do
		{
			most = Volatile.Read(ref _mostAtOnce);
		}
		while (running > most && Interlocked.CompareExchange(ref _mostAtOnce, running, most) != most);

		StartedSignal(number).SetResult();

		try
		{
			if (_holds.TryGetValue(number, out TaskCompletionSource? hold))
			{
				await hold.Task.WaitAsync(s_giveUp, cancellationToken);
			}

			if (_failures.TryGetValue(number, out Exception? failure))
			{
				throw failure;
			}
		}
		finally
		{
			Interlocked.Decrement(ref _running);
		}
	}

	private TaskCompletionSource StartedSignal(int number)
	{
		return _started.GetOrAdd(number, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
	}
}
