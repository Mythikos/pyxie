using System.Globalization;

namespace Pyxie.FakeHarness;

/// <summary>What one run of the fake harness does, parsed from its arguments.</summary>
/// <param name="Outcome">The outcome word written to the result file.</param>
/// <param name="Note">The note written to the result file.</param>
/// <param name="Report">The report written to the result file.</param>
/// <param name="Writes">Files to write in the workspace, relative path to content.</param>
/// <param name="ExitCode">The exit code.</param>
/// <param name="WriteResult">Whether to write the result file at all.</param>
/// <param name="RawResult">Text to write as the result file instead of a valid result, or <see langword="null"/>.</param>
/// <param name="SleepMilliseconds">How long to wait before doing anything.</param>
/// <param name="RequiredPrompt">The text standard input must equal, or <see langword="null"/> to accept anything.</param>
/// <param name="RecordPath">A file to append a line about the run to, or <see langword="null"/>.</param>
/// <param name="Emits">Lines to print before the outcome line, in order: the stream (<see cref="FakeRun.StandardOutput"/> or <see cref="FakeRun.StandardError"/>) and the text.</param>
/// <param name="GitCommands">The git commands to run in the workspace after the files are written, in order, each as its arguments.</param>
/// <param name="WaitForPath">A file the run waits for before doing anything, or <see langword="null"/>; it lets a test decide when an agent proceeds, without guessing at a sleep.</param>
internal sealed record FakeRun(string Outcome, string? Note, string? Report, IReadOnlyList<KeyValuePair<string, string>> Writes, int ExitCode, bool WriteResult, string? RawResult, int SleepMilliseconds, string? RequiredPrompt, string? RecordPath, IReadOnlyList<KeyValuePair<string, string>> Emits, IReadOnlyList<IReadOnlyList<string>> GitCommands, string? WaitForPath)
{
	/// <summary>The stream of an emitted line that goes to standard output.</summary>
	public const string StandardOutput = "stdout";

	/// <summary>The stream of an emitted line that goes to standard error.</summary>
	public const string StandardError = "stderr";

	/// <summary>Parses the behavior arguments.</summary>
	/// <param name="arguments">The arguments.</param>
	/// <returns>The run.</returns>
	/// <exception cref="ArgumentException">An argument is unknown or misses its value.</exception>
	public static FakeRun Parse(IReadOnlyList<string> arguments)
	{
		string outcome = "completed";
		string? note = null;
		string? report = null;
		List<KeyValuePair<string, string>> writes = [];
		int exitCode = 0;
		bool writeResult = true;
		string? rawResult = null;
		int sleep = 0;
		string? requiredPrompt = null;
		string? record = null;
		List<KeyValuePair<string, string>> emits = [];
		List<IReadOnlyList<string>> gitCommands = [];
		string? waitFor = null;

		for (int index = 0; index < arguments.Count; index++)
		{
			string name = arguments[index];
			switch (name)
			{
				case "--outcome":
					outcome = Value(arguments, ref index);
					break;
				case "--note":
					note = Value(arguments, ref index);
					break;
				case "--report":
					report = Value(arguments, ref index);
					break;
				case "--write":
					string write = Value(arguments, ref index);
					int equals = write.IndexOf('=', StringComparison.Ordinal);
					if (equals <= 0)
					{
						throw new ArgumentException($"--write takes path=content, not {write}.");
					}

					writes.Add(new KeyValuePair<string, string>(write[..equals], write[(equals + 1)..]));
					break;
				case "--exit":
					exitCode = int.Parse(Value(arguments, ref index), CultureInfo.InvariantCulture);
					break;
				case "--no-result":
					writeResult = false;
					break;
				case "--raw-result":
					rawResult = Value(arguments, ref index);
					break;
				case "--sleep":
					sleep = int.Parse(Value(arguments, ref index), CultureInfo.InvariantCulture);
					break;
				case "--require-prompt":
					requiredPrompt = Value(arguments, ref index);
					break;
				case "--record":
					record = Value(arguments, ref index);
					break;
				case "--wait-for":
					waitFor = Value(arguments, ref index);
					break;
				case "--git":
					gitCommands.Add(ScriptLine.Split(Value(arguments, ref index)));
					break;
				case "--stdout":
					emits.Add(new KeyValuePair<string, string>(StandardOutput, Value(arguments, ref index)));
					break;
				case "--stderr":
					emits.Add(new KeyValuePair<string, string>(StandardError, Value(arguments, ref index)));
					break;
				default:
					throw new ArgumentException($"Unknown argument {name}.");
			}
		}

		return new FakeRun(outcome, note, report, writes, exitCode, writeResult, rawResult, sleep, requiredPrompt, record, emits, gitCommands, waitFor);
	}

	private static string Value(IReadOnlyList<string> arguments, ref int index)
	{
		if (index + 1 >= arguments.Count)
		{
			throw new ArgumentException($"{arguments[index]} needs a value.");
		}

		index++;

		return arguments[index];
	}
}
