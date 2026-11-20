namespace Pyxie.Runtime.Sandboxes;

/// <summary>What an agent's sandbox is given so its harness can sign in and reach its model service.</summary>
/// <param name="Secrets">The files whose text the harness gets as environment variables.</param>
/// <param name="Mounts">The folders the harness keeps its sign-in in.</param>
/// <param name="Environment">The environment the harness gets.</param>
/// <param name="Problem">Why the agent cannot sign in, in words for a person; <see langword="null"/> when it can.</param>
public sealed record SandboxSignIn(
	IReadOnlyList<SandboxSecret> Secrets,
	IReadOnlyList<SandboxMount> Mounts,
	IReadOnlyDictionary<string, string> Environment,
	string? Problem)
{
	/// <summary>Gets the optional setup run in the opened container, including a reused worker, before repository code runs.</summary>
	public Func<ISandbox, CancellationToken, Task>? PrepareAsync { get; init; }

	/// <summary>Gets a sign-in that gives nothing, for harnesses Pyxie does not sign in.</summary>
	public static SandboxSignIn None { get; } = new([], [], new Dictionary<string, string>(), null);

	/// <summary>Creates a sign-in that cannot be given.</summary>
	/// <param name="problem">Why, in words for a person.</param>
	/// <returns>The sign-in.</returns>
	public static SandboxSignIn Missing(string problem)
	{
		return None with { Problem = problem };
	}
}
