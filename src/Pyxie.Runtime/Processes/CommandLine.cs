using System.Text;

namespace Pyxie.Runtime.Processes;

/// <summary>Turns the command and argument strings people configure into what a process is started with.</summary>
public static class CommandLine
{
	// Characters cmd.exe treats specially outside quotes; an argument containing one is quoted when it is passed through a batch file.
	private const string BatchSpecialCharacters = " \t&|<>^(),;=";

	/// <summary>Splits an argument string the way Windows programs parse their command line (<c>CommandLineToArgvW</c>): whitespace separates arguments, double quotes group them, <c>""</c> inside quotes is a literal quote, and backslashes are literal unless they precede a quote.</summary>
	/// <param name="arguments">The configured arguments, for example <c>exec -s workspace-write</c>.</param>
	/// <returns>The arguments, each to be passed as one.</returns>
	public static IReadOnlyList<string> Split(string arguments)
	{
		List<string> result = [];
		int index = 0;

		while (index < arguments.Length)
		{
			while (index < arguments.Length && arguments[index] is ' ' or '\t' or '\r' or '\n')
			{
				index++;
			}

			if (index >= arguments.Length)
			{
				break;
			}

			StringBuilder current = new();
			bool quoted = false;

			while (index < arguments.Length)
			{
				char character = arguments[index];

				if (character == '\\')
				{
					int start = index;
					while (index < arguments.Length && arguments[index] == '\\')
					{
						index++;
					}

					int backslashes = index - start;
					if (index < arguments.Length && arguments[index] == '"')
					{
						current.Append('\\', backslashes / 2);
						if (backslashes % 2 == 1)
						{
							current.Append('"');
							index++;
						}
					}
					else
					{
						current.Append('\\', backslashes);
					}

					continue;
				}

				if (character == '"')
				{
					if (quoted && index + 1 < arguments.Length && arguments[index + 1] == '"')
					{
						current.Append('"');
						index += 2;
						continue;
					}

					quoted = !quoted;
					index++;
					continue;
				}

				if (!quoted && character is ' ' or '\t' or '\r' or '\n')
				{
					break;
				}

				current.Append(character);
				index++;
			}

			result.Add(current.ToString());
		}

		return result;
	}

	/// <summary>
	/// Finds the file a command names: a path (relative to <paramref name="workingDirectory"/> when not rooted) or a name looked up in each <c>PATH</c> directory. On Windows a name without an extension is tried with each <c>PATHEXT</c> extension, and files without an extension are ignored, as the shell does.
	/// </summary>
	/// <param name="command">The command.</param>
	/// <param name="workingDirectory">The directory the command will run in.</param>
	/// <returns>The full path of the file, or <see langword="null"/> when none was found.</returns>
	public static string? Resolve(string command, string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return null;
		}

		bool hasDirectory = command.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || command.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(command);

		if (hasDirectory)
		{
			return FindWithExtensions(Path.GetFullPath(command, workingDirectory));
		}

		string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string directory in directories)
		{
			string? found = FindWithExtensions(Path.Combine(directory.Trim('"'), command));
			if (found is not null)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>Determines whether a file is a Windows batch file, which can only run through <c>cmd.exe</c>.</summary>
	/// <param name="path">The file.</param>
	/// <returns><see langword="true"/> for <c>.cmd</c> and <c>.bat</c> files on Windows.</returns>
	public static bool IsBatchFile(string path)
	{
		string extension = Path.GetExtension(path);

		return OperatingSystem.IsWindows()
			&& (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Builds the <c>cmd.exe</c> argument string that runs a batch file with arguments (npm installs harnesses such as Codex as <c>.cmd</c> files). <c>cmd.exe</c> expands <c>%</c> and <c>!</c> even inside quotes and cannot pass a literal quote safely, so arguments containing those characters or line breaks are refused rather than mangled.</summary>
	/// <param name="batchFile">The full path of the batch file.</param>
	/// <param name="arguments">The arguments.</param>
	/// <param name="commandLine">The argument string for <c>cmd.exe</c>.</param>
	/// <returns><see langword="null"/> on success, otherwise why the arguments cannot be passed.</returns>
	public static string? TryBuildBatchCommandLine(string batchFile, IReadOnlyList<string> arguments, out string commandLine)
	{
		commandLine = string.Empty;
		StringBuilder line = new();
		line.Append("/d /s /c \"").Append(QuoteForBatch(batchFile));

		foreach (string argument in arguments)
		{
			if (argument.IndexOfAny(['"', '%', '!', '\r', '\n']) >= 0)
			{
				return $"The argument {argument} cannot be passed to the batch file {Path.GetFileName(batchFile)}: quotes, %, !, and line breaks are not safe through cmd.exe.";
			}

			line.Append(' ').Append(QuoteForBatch(argument));
		}

		commandLine = line.Append('"').ToString();

		return null;
	}

	private static string QuoteForBatch(string argument)
	{
		if (argument.Length > 0 && argument.IndexOfAny(BatchSpecialCharacters.ToCharArray()) < 0)
		{
			return argument;
		}

		// Inside quotes, trailing backslashes would escape the closing quote for the program that finally parses the line, so they are doubled.
		int trailing = argument.Length - argument.TrimEnd('\\').Length;

		return string.Concat("\"", argument, new string('\\', trailing), "\"");
	}

	private static string? FindWithExtensions(string candidate)
	{
		if (!OperatingSystem.IsWindows())
		{
			return File.Exists(candidate) ? candidate : null;
		}

		string[] extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string extension = Path.GetExtension(candidate);

		if (extension.Length > 0 && extensions.Any(known => known.Equals(extension, StringComparison.OrdinalIgnoreCase)))
		{
			return File.Exists(candidate) ? candidate : null;
		}

		foreach (string known in extensions)
		{
			string withExtension = candidate + known;
			if (File.Exists(withExtension))
			{
				return withExtension;
			}
		}

		return null;
	}
}
