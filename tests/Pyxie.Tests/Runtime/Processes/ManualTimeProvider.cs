namespace Pyxie.Tests.Runtime.Processes;

/// <summary>A clock whose time and timers a test drives: time stands still until <see cref="Advance"/> moves it, and moving it past a timer's due time fires that timer at once. The process runner measures its timeout and duration with the clock it is given, so a test hands it this one and triggers a timeout without waiting for one.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
	private readonly Lock _gate = new();
	private readonly List<ManualTimer> _timers = [];
	private readonly TaskCompletionSource _timerScheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private TimeSpan _elapsed;

	/// <summary>Gets a task that completes once a timer with a due time exists, which is when the code under test has started waiting on the clock.</summary>
	public Task TimerScheduled => _timerScheduled.Task;

	/// <inheritdoc/>
	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	/// <inheritdoc/>
	public override long GetTimestamp()
	{
		lock (_gate)
		{
			return _elapsed.Ticks;
		}
	}

	/// <inheritdoc/>
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ManualTimer timer = new(this, callback, state);
		lock (_gate)
		{
			_timers.Add(timer);
		}

		timer.Change(dueTime, period);

		return timer;
	}

	/// <summary>Moves the clock forward and fires every timer that has become due, in the order they fall due.</summary>
	/// <param name="by">How far to move the clock.</param>
	public void Advance(TimeSpan by)
	{
		List<ManualTimer> due;
		lock (_gate)
		{
			_elapsed += by;
			due = [.. _timers.Where(timer => timer.IsDueAt(_elapsed)).OrderBy(timer => timer.DueAt)];
		}

		foreach (ManualTimer timer in due)
		{
			timer.Fire();
		}
	}

	internal TimeSpan Elapsed
	{
		get
		{
			lock (_gate)
			{
				return _elapsed;
			}
		}
	}

	internal void Remove(ManualTimer timer)
	{
		lock (_gate)
		{
			_timers.Remove(timer);
		}
	}

	internal void NotifyScheduled()
	{
		_timerScheduled.TrySetResult();
	}
}
