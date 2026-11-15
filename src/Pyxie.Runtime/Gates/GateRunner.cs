using System.Globalization;
using System.Text;

using Pyxie.Data.Entities;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Runtime.Gates;

/// <summary>Runs a gate round: the enabled gates in order in the card's repository in its gate sandbox, stopping at the first that exits with a non-zero code or times out. Gate commands run the agent's code, so they never run on the host. A failing gate sends the work back with its output; a gate that cannot start is a configuration problem for a person.</summary>
public sealed class GateRunner
{
	// Gate output goes to the worker; this much of each gate's output is kept, its beginning and end.
	private const int MaxCapturedOutput = 80_000;

	/// <summary>Runs gates in a sandbox.</summary>
	/// <param name="gates">The enabled gates, in the order they run.</param>
	/// <param name="sandbox">The card's gate sandbox.</param>
	/// <param name="repository">The card's repository, as the sandbox sees it.</param>
	/// <param name="logPath">Where the gates' full output is kept.</param>
	/// <param name="cancellationToken">A token that stops the running gate when cancelled.</param>
	/// <returns>Passed with a summary, failed with the failing gate's output, or blocked when a gate cannot start.</returns>
	/// <exception cref="OperationCanceledException">The token was cancelled; the running gate has been stopped.</exception>
	public async Task<RoundVerdict> RunAsync(IReadOnlyList<Gate> gates, ISandbox sandbox, string repository, string logPath, CancellationToken cancellationToken)
	{
		if (gates.Count == 0)
		{
			return RoundVerdict.Succeeded("No gates are enabled.");
		}

		StringBuilder summary = new();
		foreach (Gate gate in gates)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());
			await File.AppendAllTextAsync(logPath, $"== {gate.Name}: {gate.Command} {gate.Arguments}\n", cancellationToken);

			SandboxCommand command = new(gate.Command, CommandLine.Split(gate.Arguments), repository)
			{
				Timeout = TimeSpan.FromMinutes(gate.TimeoutMinutes),
				LogPath = logPath,
				MaxOutputLength = MaxCapturedOutput,
			};

			ProcessResult result = await sandbox.RunAsync(command, cancellationToken);
			string duration = Duration.Describe(result.Duration);

			switch (result.Outcome)
			{
				case ProcessOutcome.NotStarted:
					summary.Append(CultureInfo.InvariantCulture, $"- {gate.Name}: could not start").AppendLine();
					return RoundVerdict.Blocked($"Gate '{gate.Name}' could not start: {result.StartError}", summary.ToString().TrimEnd());

				case ProcessOutcome.TimedOut:
					string limit = Duration.Describe(TimeSpan.FromMinutes(gate.TimeoutMinutes));
					summary.Append(CultureInfo.InvariantCulture, $"- {gate.Name}: timed out after {limit}").AppendLine();
					return RoundVerdict.Failed($"Gate '{gate.Name}' timed out after {limit}.", WithOutput(summary, gate, result));

				case ProcessOutcome.Exited when result.ExitCode != 0:
					summary.Append(CultureInfo.InvariantCulture, $"- {gate.Name}: failed with exit code {result.ExitCode} in {duration}").AppendLine();
					return RoundVerdict.Failed(string.Create(CultureInfo.InvariantCulture, $"Gate '{gate.Name}' failed with exit code {result.ExitCode}."), WithOutput(summary, gate, result));

				default:
					summary.Append(CultureInfo.InvariantCulture, $"- {gate.Name}: passed in {duration}").AppendLine();
					break;
			}
		}

		string note = gates.Count == 1 ? "1 gate passed." : string.Create(CultureInfo.InvariantCulture, $"{gates.Count} gates passed.");

		return RoundVerdict.Succeeded(note, summary.ToString().TrimEnd());
	}

	private static string WithOutput(StringBuilder summary, Gate gate, ProcessResult result)
	{
		string output = result.Output.Trim();

		return $"{summary.ToString().TrimEnd()}\n\n## {gate.Name} output\n\n{(output.Length == 0 ? "(no output)" : output)}";
	}
}
