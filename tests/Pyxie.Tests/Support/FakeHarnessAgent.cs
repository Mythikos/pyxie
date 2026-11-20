using System.Text;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Tests.Support;

/// <summary>Builds agents (and gate commands) that run <c>Pyxie.FakeHarness</c>, the stand-in harness, so runtime tests never start a real agent. The fake harness is started the way every agent is: command <c>dotnet</c>, with the harness's dll and then the behavior arguments as the configured arguments. Its behavior arguments are <c>--outcome word</c>, <c>--note text</c>, <c>--report text</c>, <c>--write path=content</c>, <c>--git arguments</c> (one git command run in the workspace once the files are written, such as <c>--git "add --all"</c>; the arguments are split like a script line, so whitespace separates them and double quotes group them; repeat it for each command, which run in the order given; a command that fails ends the run with its exit code), <c>--exit code</c>, <c>--no-result</c>, <c>--raw-result text</c>, <c>--sleep ms</c>, <c>--wait-for file</c> (wait until the file exists before doing anything, so a test decides when the agent proceeds), <c>--require-prompt text</c>, <c>--record file</c>, and <c>--stdout text</c> and <c>--stderr text</c> (lines printed in the order given, before the outcome line, each followed by a short pause so the order they arrive in is the order they were written); read <c>tests/Pyxie.FakeHarness/FakeRun.cs</c>. With none, it writes a <c>completed</c> result and exits 0.</summary>
/// <remarks>
/// <para>
/// Arguments are passed as separate strings and quoted here, so a note such as <c>needs a test</c> stays one argument. A configured arguments string is limited to <c>PyxieLimits.MaxArgumentsLength</c> characters, so keep long text in a <c>--write</c> file or a script rather than on the command line.
/// </para>
/// </remarks>
public static class FakeHarnessAgent
{
	/// <summary>The command every fake agent runs. It is looked up on <c>PATH</c> like any agent command.</summary>
	public const string Command = "dotnet";

	private const string DllName = "Pyxie.FakeHarness.dll";
	private const string RuntimeConfigName = "Pyxie.FakeHarness.runtimeconfig.json";
	private const string ScriptOption = "--script";
	private const string DefaultRun = "--outcome completed";

	/// <summary>Gets the full path of <c>Pyxie.FakeHarness.dll</c> in the test output. The project reference puts it there with its <c>.deps.json</c> and <c>.runtimeconfig.json</c>, which is all <c>dotnet &lt;dll&gt;</c> needs.</summary>
	/// <exception cref="InvalidOperationException">The dll or its runtime configuration is missing from the test output, so the fake harness could not run.</exception>
	public static string DllPath
	{
		get
		{
			string dll = Path.Combine(AppContext.BaseDirectory, DllName);
			string runtimeConfig = Path.Combine(AppContext.BaseDirectory, RuntimeConfigName);
			if (!File.Exists(dll) || !File.Exists(runtimeConfig))
			{
				throw new InvalidOperationException($"{DllName} and {RuntimeConfigName} must be in {AppContext.BaseDirectory}; Pyxie.Tests references Pyxie.FakeHarness to copy them there.");
			}

			return dll;
		}
	}

	/// <summary>Builds the configured arguments string of a fake agent: the harness dll, then the behavior arguments.</summary>
	/// <param name="fakeArguments">The behavior arguments, each one argument: <c>["--outcome", "rejected", "--note", "needs a test"]</c>.</param>
	/// <returns>The arguments string, quoted so the runtime's argument splitting gives back exactly these arguments.</returns>
	public static string Arguments(params string[] fakeArguments)
	{
		List<string> parts = [Quote(DllPath), .. fakeArguments.Select(Quote)];
		return string.Join(' ', parts);
	}

	/// <summary>Builds the configured arguments string of a fake agent that takes its behavior from a script (see <see cref="WriteScriptAsync"/>).</summary>
	/// <param name="scriptPath">The script file.</param>
	/// <returns>The arguments string.</returns>
	public static string ScriptArguments(string scriptPath)
	{
		return $"{Quote(DllPath)} {ScriptOption} {Quote(scriptPath)}";
	}

	/// <summary>Builds the snapshot of a fake agent, as a round records it, for tests that hand the runtime a round directly.</summary>
	/// <param name="name">The agent name.</param>
	/// <param name="harness">The harness kind.</param>
	/// <param name="fakeArguments">The behavior arguments, each one argument.</param>
	/// <returns>The snapshot, which runs <c>dotnet</c> on the fake harness.</returns>
	public static AgentSnapshot Snapshot(string name, HarnessKind harness, params string[] fakeArguments)
	{
		return new AgentSnapshot(name, harness, Command, Arguments(fakeArguments), harness == HarnessKind.Other ? AgentAuthentication.None : AgentAuthentication.Account);
	}

	/// <summary>Builds the snapshot of a fake agent that takes its behavior from a script.</summary>
	/// <param name="name">The agent name.</param>
	/// <param name="harness">The harness kind.</param>
	/// <param name="scriptPath">The script file from <see cref="WriteScriptAsync"/>.</param>
	/// <returns>The snapshot.</returns>
	public static AgentSnapshot ScriptSnapshot(string name, HarnessKind harness, string scriptPath)
	{
		return new AgentSnapshot(name, harness, Command, ScriptArguments(scriptPath), harness == HarnessKind.Other ? AgentAuthentication.None : AgentAuthentication.Account);
	}

	/// <summary>Builds the input that creates a fake agent through the agent service.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="harness">The harness kind.</param>
	/// <param name="canWork">Whether the agent may be a card's worker.</param>
	/// <param name="canReview">Whether the agent may be a card's reviewer.</param>
	/// <param name="fakeArguments">The behavior arguments, each one argument.</param>
	/// <returns>The agent input, which runs <c>dotnet</c> on the fake harness.</returns>
	public static AgentInput Input(string name, HarnessKind harness, bool canWork, bool canReview, params string[] fakeArguments)
	{
		return new AgentInput(name, harness, Command, Arguments(fakeArguments), canWork, canReview);
	}

	/// <summary>Builds the input that creates a fake agent whose behavior comes from a script.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="harness">The harness kind.</param>
	/// <param name="canWork">Whether the agent may be a card's worker.</param>
	/// <param name="canReview">Whether the agent may be a card's reviewer.</param>
	/// <param name="scriptPath">The script file from <see cref="WriteScriptAsync"/>.</param>
	/// <returns>The agent input.</returns>
	public static AgentInput ScriptInput(string name, HarnessKind harness, bool canWork, bool canReview, string scriptPath)
	{
		return new AgentInput(name, harness, Command, ScriptArguments(scriptPath), canWork, canReview);
	}

	/// <summary>Writes a script for an agent that behaves differently on each run, such as a reviewer that rejects once and then approves. Run <em>n</em> takes the arguments of line <em>n</em>; once the runs are used up, the last one repeats. The fake harness counts runs in a file next to the script, which is not safe for agents that run at once, so use scripts with one pipeline worker.</summary>
	/// <param name="folder">An existing folder for the script. Keep it outside every repository and card workspace (see <c>RuntimeHarness.WriteScriptAsync</c>), or the worker's commit and git's cleaning would touch the counter.</param>
	/// <param name="runs">The behavior arguments of each run, each one argument: <c>["--outcome", "rejected", "--note", "needs a test"], ["--outcome", "approved"]</c>. An empty run stands for the default behavior.</param>
	/// <returns>The full path of the script, for <see cref="ScriptSnapshot"/> and <see cref="ScriptInput"/>.</returns>
	/// <exception cref="ArgumentException">An argument contains a double quote, which a script line cannot express (a <c>--raw-result</c> of JSON, for one).</exception>
	public static async Task<string> WriteScriptAsync(string folder, params string[][] runs)
	{
		string path = Path.Combine(folder, $"script-{Guid.NewGuid().ToString("N")[..12]}.txt");
		string[] lines = [.. runs.Select(run => run.Length == 0 ? DefaultRun : string.Join(' ', run.Select(ScriptArgument)))];
		await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

		return path;
	}

	// A script line splits on whitespace and groups with double quotes, with no way to escape a quote.
	private static string ScriptArgument(string argument)
	{
		if (argument.Contains('"', StringComparison.Ordinal))
		{
			throw new ArgumentException($"The script argument {argument} contains a double quote, which a script line cannot express.", nameof(argument));
		}

		return argument.Length > 0 && !argument.Any(char.IsWhiteSpace) ? argument : $"\"{argument}\"";
	}

	// Quotes an argument the way Windows programs parse their command line, which is how the runtime splits configured arguments (CommandLine.Split): backslashes are literal except in front of a quote, where they are doubled, and a quote inside the argument is escaped with a backslash.
	private static string Quote(string argument)
	{
		if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\r', '\n', '"']) < 0)
		{
			return argument;
		}

		StringBuilder quoted = new("\"");
		int backslashes = 0;

		foreach (char character in argument)
		{
			if (character == '\\')
			{
				backslashes++;
			}
			else if (character == '"')
			{
				quoted.Append('\\', (backslashes * 2) + 1).Append('"');
				backslashes = 0;
			}
			else
			{
				quoted.Append('\\', backslashes).Append(character);
				backslashes = 0;
			}
		}

		return quoted.Append('\\', backslashes * 2).Append('"').ToString();
	}
}
