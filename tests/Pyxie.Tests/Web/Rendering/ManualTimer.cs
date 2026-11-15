namespace Pyxie.Tests.Web.Rendering;

/// <summary>A timer that fires only when <see cref="Fire"/> is called, until it is disposed.</summary>
internal sealed class ManualTimer : ITimer
{
	private readonly TimerCallback _callback;
	private readonly object? _state;

	/// <summary>Initializes a new instance of the <see cref="ManualTimer"/> class.</summary>
	/// <param name="callback">The method to call when the timer fires.</param>
	/// <param name="state">The value passed to the callback.</param>
	public ManualTimer(TimerCallback callback, object? state)
	{
		_callback = callback;
		_state = state;
	}

	/// <summary>Gets a value indicating whether the timer was disposed, after which it never fires.</summary>
	public bool IsDisposed { get; private set; }

	/// <summary>Calls the callback, unless the timer was disposed.</summary>
	public void Fire()
	{
		if (!IsDisposed)
		{
			_callback(_state);
		}
	}

	/// <inheritdoc/>
	public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;

	/// <inheritdoc/>
	public void Dispose()
	{
		IsDisposed = true;
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		Dispose();

		return ValueTask.CompletedTask;
	}
}
