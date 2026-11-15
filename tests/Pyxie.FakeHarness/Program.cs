using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pyxie.FakeHarness;

/// <summary>A stand-in agent harness for tests: it behaves as its arguments say (write files, run git, wait for a file, report an outcome, exit with a code, hang) so the runtime can be tested without a real agent. With <c>--script file</c>, each run takes its arguments from the next line of the file, so one agent can, say, be rejected and then approved.</summary>
internal static class Program
{
	private const string ResultFile = ".pyxie/result.json";

	// The runner reads standard output and standard error on threads of their own, so lines written back to back could be seen in either order; pausing after each line makes the order they were written the order they arrive.
	private const int EmitPauseMilliseconds = 250;

	// A run that waits for a file gives up after this long, so a test that never creates it fails instead of hanging.
	private const int WaitForPollMilliseconds = 50;
	private static readonly TimeSpan s_maxWaitFor = TimeSpan.FromMinutes(5);

	private static async Task<int> Main(string[] args)
	{
		string input = Console.IsInputRedirected ? await Console.In.ReadToEndAsync() : string.Empty;
		IReadOnlyList<string> arguments = args.Length == 2 && args[0] == "--script" ? NextScriptLine(args[1]) : args;
		FakeRun run = FakeRun.Parse(arguments);

		if (run.RecordPath is not null)
		{
			string line = JsonSerializer.Serialize(new { directory = Environment.CurrentDirectory, arguments, input });
			await File.AppendAllTextAsync(run.RecordPath, line + Environment.NewLine);
		}

		if (run.RequiredPrompt is not null && input.Trim() != run.RequiredPrompt)
		{
			await Console.Error.WriteLineAsync($"Expected the prompt on standard input; got: {input}");
			return 64;
		}

		if (run.WaitForPath is not null)
		{
			DateTime giveUp = DateTime.UtcNow + s_maxWaitFor;
			while (!File.Exists(run.WaitForPath))
			{
				if (DateTime.UtcNow > giveUp)
				{
					await Console.Error.WriteAsync($"Gave up waiting for {run.WaitForPath}.");
					return 66;
				}

				await Task.Delay(WaitForPollMilliseconds);
			}
		}

		if (run.SleepMilliseconds > 0)
		{
			await Task.Delay(run.SleepMilliseconds);
		}

		foreach ((string path, string content) in run.Writes)
		{
			string full = Path.GetFullPath(path);
			Directory.CreateDirectory(Path.GetDirectoryName(full) ?? Environment.CurrentDirectory);
			await File.WriteAllTextAsync(full, content);
		}

		foreach (IReadOnlyList<string> git in run.GitCommands)
		{
			int code = await RunGitAsync(git);
			if (code != 0)
			{
				return code;
			}
		}

		foreach ((string stream, string text) in run.Emits)
		{
			TextWriter writer = stream == FakeRun.StandardError ? Console.Error : Console.Out;
			await writer.WriteLineAsync(text);
			await writer.FlushAsync();
			await Task.Delay(EmitPauseMilliseconds);
		}

		await Console.Out.WriteLineAsync($"fake harness: {run.Outcome}");

		if (run.RawResult is not null)
		{
			await WriteResultAsync(run.RawResult);
		}
		else if (run.WriteResult)
		{
			await WriteResultAsync(JsonSerializer.Serialize(new { outcome = run.Outcome, note = run.Note, report = run.Report }));
		}

		return run.ExitCode;
	}

	// Runs git in the workspace the way an agent that ran git would, with signing off so a machine's own git settings never get in the way. Its output goes to the console; a command that fails ends the run with its exit code, so a test that got a command wrong fails loudly.
	private static async Task<int> RunGitAsync(IReadOnlyList<string> arguments)
	{
		ProcessStartInfo start = new("git") { RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (string argument in new[] { "-c", "commit.gpgsign=false" }.Concat(arguments))
		{
			start.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> errors = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();

		await Console.Out.WriteAsync(await output);
		await Console.Error.WriteAsync(await errors);

		return process.ExitCode;
	}

	private static async Task WriteResultAsync(string text)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ResultFile)) ?? Environment.CurrentDirectory);
		await File.WriteAllTextAsync(ResultFile, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	// Runs take the script's lines in turn; once they run out, the last line repeats. The count lives next to the script.
	private static IReadOnlyList<string> NextScriptLine(string script)
	{
		string[] lines = [.. File.ReadAllLines(script).Where(line => line.Trim().Length > 0)];
		string counter = script + ".count";
		int run = File.Exists(counter) ? int.Parse(File.ReadAllText(counter).Trim(), CultureInfo.InvariantCulture) : 0;
		File.WriteAllText(counter, (run + 1).ToString(CultureInfo.InvariantCulture));

		return ScriptLine.Split(lines[Math.Min(run, lines.Length - 1)]);
	}
}
