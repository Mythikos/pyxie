using Pyxie.Tests.Support;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A clock a test controls on both counts: the time it reads stays where the test puts it, and its timers fire only when the test fires them. A component that shows an elapsed time and refreshes it on a timer is then checked without waiting.</summary>
internal sealed class ControlledTimeProvider : TimeProvider
{
	private readonly ManualTimeProvider _timers = new();
	private readonly List<TimeSpan> _periods = [];

	/// <summary>Initializes a new instance of the <see cref="ControlledTimeProvider"/> class.</summary>
	/// <param name="utcNow">The first instant; <see cref="TestClock"/>'s default unless given.</param>
	public ControlledTimeProvider(DateTime? utcNow = null)
	{
		Clock = new TestClock(utcNow);
	}

	/// <summary>Gets the clock the time is read from, which a test moves forward.</summary>
	public TestClock Clock { get; }

	/// <summary>Gets the period of every timer that was created, in the order they were created.</summary>
	public IReadOnlyList<TimeSpan> Periods => _periods;

	/// <summary>Gets how many timers were created and not disposed.</summary>
	public int PendingTimers => _timers.PendingTimers;

	/// <summary>Fires every timer that has not been disposed, once.</summary>
	public void FireTimers()
	{
		_timers.FireTimers();
	}

	/// <inheritdoc/>
	public override DateTimeOffset GetUtcNow()
	{
		return Clock.GetUtcNow();
	}

	/// <inheritdoc/>
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		_periods.Add(period);

		return _timers.CreateTimer(callback, state, dueTime, period);
	}
}
