namespace Pyxie.Tests.Web.Rendering;

/// <summary>A clock whose timers never fire on their own: a test fires them when it chooses, so what a component shows before and after a delay is checked without waiting.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];

	/// <summary>Gets how many timers were created and not disposed.</summary>
	public int PendingTimers => _timers.Count(timer => !timer.IsDisposed);

	/// <summary>Fires every timer that has not been disposed, once.</summary>
	public void FireTimers()
	{
		foreach (ManualTimer timer in _timers.Where(timer => !timer.IsDisposed).ToList())
		{
			timer.Fire();
		}
	}

	/// <inheritdoc/>
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ManualTimer timer = new(callback, state);
		_timers.Add(timer);

		return timer;
	}
}
