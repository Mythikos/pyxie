namespace Pyxie.Runtime.Processes;

/// <summary>A command for <see cref="ProcessRunner"/> to run to completion.</summary>
/// <param name="Command">The executable: a name looked up on <c>PATH</c> (with <c>PATHEXT</c> on Windows), or a path, relative to <paramref name="WorkingDirectory"/> when not rooted.</param>
/// <param name="Arguments">The arguments, each passed to the process as one argument.</param>
/// <param name="WorkingDirectory">The directory the process starts in.</param>
public sealed record ProcessRequest(string Command, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
	/// <summary>Gets the text written to the process's standard input, which is then closed. <see langword="null"/> closes it at once.</summary>
	public string? StandardInput { get; init; }

	/// <summary>Gets how long the process may run before it is killed, with its children.</summary>
	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

	/// <summary>Gets the file the full output is appended to as it arrives, or <see langword="null"/> to keep only the captured excerpt.</summary>
	public string? LogPath { get; init; }

	/// <summary>Gets the most output characters kept in <see cref="ProcessResult.Output"/>: the beginning and the end, with a marker for what was cut between them.</summary>
	public int MaxOutputLength { get; init; } = 200_000;

	/// <summary>Gets environment variables to set for the process, on top of Pyxie's own environment.</summary>
	public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
