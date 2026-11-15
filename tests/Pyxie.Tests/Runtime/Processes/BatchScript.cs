using System.Text;

using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>Writes the Windows batch files that tests run: the only way to get a process that writes to both output streams, reads the environment, or starts children without compiling a program.</summary>
public static class BatchScript
{
	/// <summary>Writes a batch file with the CRLF line breaks cmd.exe expects and no byte order mark, which cmd would read as part of the first command.</summary>
	/// <param name="folder">The folder to write it in.</param>
	/// <param name="name">The file name, ending in <c>.cmd</c> or <c>.bat</c>.</param>
	/// <param name="lines">The lines of the script.</param>
	/// <returns>The full path of the file.</returns>
	public static async Task<string> WriteAsync(string folder, string name, params string[] lines)
	{
		string path = Path.Combine(folder, name);
		await File.WriteAllTextAsync(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), TestContext.Current.CancellationToken);

		return path;
	}

	/// <summary>Writes a batch file that runs the fake harness with the arguments the batch file itself was given (<c>%*</c>), so a test sees exactly what survived cmd.exe.</summary>
	/// <param name="folder">The folder to write it in.</param>
	/// <param name="name">The file name, ending in <c>.cmd</c> or <c>.bat</c>.</param>
	/// <returns>The full path of the file.</returns>
	public static async Task<string> WriteFakeHarnessLauncherAsync(string folder, string name)
	{
		return await WriteAsync(folder, name, $"@\"{FakeHarnessAgent.Command}\" \"{FakeHarnessAgent.DllPath}\" %*");
	}
}
