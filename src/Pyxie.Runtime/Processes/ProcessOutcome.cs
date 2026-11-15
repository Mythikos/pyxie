namespace Pyxie.Runtime.Processes;

/// <summary>How a process run ended.</summary>
public enum ProcessOutcome
{
	/// <summary>The process exited by itself; see <see cref="ProcessResult.ExitCode"/>.</summary>
	Exited = 0,

	/// <summary>The process ran past its timeout and was killed with its children.</summary>
	TimedOut = 1,

	/// <summary>The process could not be started, for example because the command was not found.</summary>
	NotStarted = 2,
}
