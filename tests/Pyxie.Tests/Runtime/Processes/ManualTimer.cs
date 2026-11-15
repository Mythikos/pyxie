namespace Pyxie.Tests.Runtime.Processes;

/// <summary>A timer of a <see cref="ManualTimeProvider"/>: it fires when the provider's clock is advanced to or past its due time, on the thread that advances it.</summary>
public sealed class ManualTimer : ITimer
{
	private readonly ManualTimeProvider _provider;
	private readonly TimerCallback _callback;
	private readonly object? _state;
	private readonly Lock _gate = new();
	private TimeSpan? _dueAt;
	private TimeSpan _period = Timeout.InfiniteTimeSpan;

	internal ManualTimer(ManualTimeProvider provider, TimerCallback callback, object? state)
	{
		_provider = provider;
		_callback = callback;
		_state = state;
	}

	internal TimeSpan? DueAt
	{
		get
		{
			lock (_gate)
			{
				return _dueAt;
			}
		}
	}

	/// <inheritdoc/>
	public bool Change(TimeSpan dueTime, TimeSpan period)
	{
		lock (_gate)
		{
			_period = period;
			_dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _provider.Elapsed + dueTime;
		}

		if (dueTime != Timeout.InfiniteTimeSpan)
		{
			_provider.NotifyScheduled();
		}

		return true;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		lock (_gate)
		{
			_dueAt = null;
		}

		_provider.Remove(this);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		Dispose();

		return ValueTask.CompletedTask;
	}

	internal bool IsDueAt(TimeSpan now)
	{
		lock (_gate)
		{
			return _dueAt is TimeSpan due && due <= now;
		}
	}

	internal void Fire()
	{
		lock (_gate)
		{
			if (_dueAt is null)
			{
				return;
			}

			_dueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : _dueAt + _period;
		}

		_callback(_state);
	}
}
