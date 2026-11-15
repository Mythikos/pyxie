namespace Pyxie.Tests.Support;

/// <summary>A clock a test controls: it stays at one instant until the test sets or advances it, so stamped times can be asserted exactly.</summary>
public sealed class TestClock : TimeProvider
{
	private DateTime _utcNow;

	/// <summary>Initializes a new instance of the <see cref="TestClock"/> class.</summary>
	/// <param name="utcNow">The first instant; 2030-01-01T00:00:00Z unless given.</param>
	public TestClock(DateTime? utcNow = null)
	{
		_utcNow = DateTime.SpecifyKind(utcNow ?? new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTimeKind.Utc);
	}

	/// <summary>Gets the current instant as a UTC <see cref="DateTime"/>.</summary>
	public DateTime UtcNow => _utcNow;

	/// <summary>Moves the clock forward.</summary>
	/// <param name="by">How far to move it.</param>
	/// <returns>The new instant.</returns>
	public DateTime Advance(TimeSpan by)
	{
		_utcNow += by;

		return _utcNow;
	}

	/// <summary>Moves the clock forward one minute, the usual gap between two operations a test tells apart.</summary>
	/// <returns>The new instant.</returns>
	public DateTime Tick()
	{
		return Advance(TimeSpan.FromMinutes(1));
	}

	/// <inheritdoc/>
	public override DateTimeOffset GetUtcNow()
	{
		return new DateTimeOffset(_utcNow, TimeSpan.Zero);
	}
}
