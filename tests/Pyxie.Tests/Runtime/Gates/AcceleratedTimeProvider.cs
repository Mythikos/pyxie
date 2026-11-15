namespace Pyxie.Tests.Runtime.Gates;

/// <summary>A clock whose timers fire sooner than asked: every due time and period is divided by a factor, so a gate's one-minute timeout elapses in a second while the gate still reports the minute it was configured with. Reading the time (and measuring how long something ran) is the system's.</summary>
public sealed class AcceleratedTimeProvider : TimeProvider
{
	private readonly int _factor;

	/// <summary>Initializes a new instance of the <see cref="AcceleratedTimeProvider"/> class.</summary>
	/// <param name="factor">How many times sooner timers fire; at least 1.</param>
	public AcceleratedTimeProvider(int factor)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
		_factor = factor;
	}

	/// <inheritdoc/>
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		return base.CreateTimer(callback, state, Shorten(dueTime), Shorten(period));
	}

	private TimeSpan Shorten(TimeSpan duration)
	{
		return duration == Timeout.InfiniteTimeSpan ? duration : duration / _factor;
	}
}
