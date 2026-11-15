namespace Pyxie.Runtime.Processes;

/// <summary>What happened when a process ran.</summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="ExitCode">The exit code when the process exited by itself; otherwise -1.</param>
/// <param name="Output">Standard output and standard error as they arrived, shortened to <see cref="ProcessRequest.MaxOutputLength"/> with the beginning and end kept. For people to read.</param>
/// <param name="Duration">How long the process ran.</param>
/// <param name="StartError">Why the process could not start, for <see cref="ProcessOutcome.NotStarted"/>; otherwise <see langword="null"/>.</param>
public sealed record ProcessResult(ProcessOutcome Outcome, int ExitCode, string Output, TimeSpan Duration, string? StartError)
{
	/// <summary>Gets standard output alone, shortened the same way as <see cref="Output"/>. Programs parse this: warnings a command prints on standard error never mix into its answer.</summary>
	public string StandardOutput { get; init; } = string.Empty;

	/// <summary>Gets a value indicating whether the process exited by itself with exit code 0.</summary>
	public bool Succeeded => Outcome == ProcessOutcome.Exited && ExitCode == 0;
}
