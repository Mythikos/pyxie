using System.Text;

using Pyxie.Tests.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>Writes a stand-in <c>docker</c> command that prints what it is told and exits with a code, whatever it is asked, so a test can put it first on <c>PATH</c> and see how <c>DockerSandboxes</c> reads an engine that is down or runs the wrong kind of containers. It is a batch file on Windows and a shell script elsewhere.</summary>
public static class FakeDocker
{
	/// <summary>Writes the command into a folder.</summary>
	/// <param name="folder">The folder to write it in, which the test puts first on <c>PATH</c>.</param>
	/// <param name="standardOutput">A line to print on standard output; empty for none. It must not hold characters the shell or cmd.exe treats specially.</param>
	/// <param name="standardError">A line to print on standard error; empty for none, with the same limits.</param>
	/// <param name="exitCode">The exit code.</param>
	/// <returns>A task that completes when the command is written.</returns>
	public static async Task WriteAsync(string folder, string standardOutput, string standardError, int exitCode)
	{
		if (OperatingSystem.IsWindows())
		{
			List<string> lines = ["@echo off"];
			if (standardOutput.Length > 0)
			{
				lines.Add($"echo {standardOutput}");
			}

			if (standardError.Length > 0)
			{
				lines.Add($"echo {standardError} 1>&2");
			}

			lines.Add($"exit /b {exitCode}");
			await BatchScript.WriteAsync(folder, "docker.cmd", [.. lines]);

			return;
		}

		StringBuilder script = new("#!/bin/sh\n");
		if (standardOutput.Length > 0)
		{
			script.Append("echo '").Append(standardOutput).Append("'\n");
		}

		if (standardError.Length > 0)
		{
			script.Append("echo '").Append(standardError).Append("' >&2\n");
		}

		script.Append("exit ").Append(exitCode).Append('\n');
		string path = Path.Combine(folder, "docker");
		await File.WriteAllTextAsync(path, script.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), TestContext.Current.CancellationToken);
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}
}
