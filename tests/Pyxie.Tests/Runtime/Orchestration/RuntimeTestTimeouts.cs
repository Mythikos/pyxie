namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>The limits runtime tests work within, so a hang fails with a message instead of stalling the run.</summary>
public static class RuntimeTestTimeouts
{
	/// <summary>The most a whole test that runs processes may take, in milliseconds. It is the backstop for a hang that no wait notices; the waits below fail first, with the state of the board.</summary>
	public const int TestMilliseconds = 180_000;

	/// <summary>The longest a test waits for the pipeline to make progress before it fails. A scenario of several rounds finishes in about a second on an idle machine but took over a minute on a busy one (each round starts a dozen processes), so the limit is generous; it stays below <see cref="TestMilliseconds"/> so the wait fails first.</summary>
	public static readonly TimeSpan Wait = TimeSpan.FromSeconds(150);

	/// <summary>How long a test watches a pipeline that should do nothing. It is longer than two poll intervals of <c>RuntimeHarness</c>, so an idle worker that wrongly looked for work would have found it.</summary>
	public static readonly TimeSpan IdleObservation = TimeSpan.FromSeconds(2.5);
}
