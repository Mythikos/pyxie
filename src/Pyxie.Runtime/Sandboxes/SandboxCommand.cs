using System.Collections.Frozen;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>A command to run in a sandbox, as the sandbox's unprivileged user.</summary>
/// <param name="Command">The program, as the sandbox finds it.</param>
/// <param name="Arguments">The arguments, each one argument.</param>
/// <param name="WorkingDirectory">The folder to run it in, as the sandbox sees it.</param>
public sealed record SandboxCommand(string Command, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
	/// <summary>Gets the text written to the command's standard input, which is then closed; <see langword="null"/> for none.</summary>
	public string? StandardInput { get; init; }

	/// <summary>Gets the environment variables set for this command alone, on top of the sandbox's own and winning over variables of the same name there, such as the image's; none by default. A sandbox may pass them on a command line that others on the machine can read, so they are never secrets.</summary>
	public IReadOnlyDictionary<string, string> Environment { get; init; } = FrozenDictionary<string, string>.Empty;

	/// <summary>Gets how long the command may run before it is stopped.</summary>
	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

	/// <summary>Gets the file the command's full output is appended to; <see langword="null"/> to keep none.</summary>
	public string? LogPath { get; init; }

	/// <summary>Gets how much of the output the result keeps.</summary>
	public int MaxOutputLength { get; init; } = 200_000;
}
