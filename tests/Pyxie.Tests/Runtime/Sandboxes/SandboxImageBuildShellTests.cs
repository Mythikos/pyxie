using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>The base image's Dockerfile and <see cref="SandboxNames"/> agree on the shell every step of a Pyxie image runs with, which is how Pyxie finds its own build cache among everything else built on the machine: the Dockerfile links <see cref="SandboxNames.BuildShell"/> to <c>/bin/sh</c> in its first <c>RUN</c> and switches to it before any other, so Docker records it with every later <c>RUN</c> step.</summary>
public sealed class SandboxImageBuildShellTests
{
	private const string Run = "RUN";
	private const string Shell = "SHELL";

	// The Dockerfile's instructions with comments, blank lines, and line continuations folded away, as Docker reads them.
	private static List<(string Keyword, string Arguments)> Instructions()
	{
		List<(string Keyword, string Arguments)> instructions = [];
		string pending = string.Empty;
		foreach (string line in SandboxImageFiles.Files["Dockerfile"].Split('\n').Select(text => text.Trim()))
		{
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			pending = pending.Length == 0 ? line.TrimEnd('\\').Trim() : $"{pending} {line.TrimEnd('\\').Trim()}";
			if (line.EndsWith('\\'))
			{
				continue;
			}

			string[] parts = pending.Split(' ', 2);
			instructions.Add((parts[0].ToUpperInvariant(), parts.Length > 1 ? parts[1] : string.Empty));
			pending = string.Empty;
		}

		return instructions;
	}

	[Fact]
	public void BuildShell_IsAnAbsolutePathOfItsOwnThatDoesNotShadowTheSystemShell()
	{
		Assert.StartsWith("/", SandboxNames.BuildShell, StringComparison.Ordinal);
		Assert.NotEqual("/bin/sh", SandboxNames.BuildShell);
	}

	[Fact]
	public void Dockerfile_TheFirstRun_LinksTheBuildShellToTheSystemShell()
	{
		(string keyword, string arguments) = Instructions().First(instruction => instruction.Keyword == Run);

		Assert.Equal((Run, $"ln -s /bin/sh {SandboxNames.BuildShell}"), (keyword, arguments));
	}

	[Fact]
	public void Dockerfile_TheInstructionAfterTheLink_SwitchesTheShellToTheBuildShell()
	{
		List<(string Keyword, string Arguments)> instructions = Instructions();
		int link = instructions.FindIndex(instruction => instruction.Keyword == Run);

		Assert.Equal((Shell, $"[\"{SandboxNames.BuildShell}\", \"-c\"]"), instructions[link + 1]);
	}

	[Fact]
	public void Dockerfile_EveryRunAfterTheLink_ComesAfterTheShellSwitch()
	{
		List<(string Keyword, string Arguments)> instructions = Instructions();
		int link = instructions.FindIndex(instruction => instruction.Keyword == Run);
		int shell = instructions.FindIndex(instruction => instruction.Keyword == Shell);
		List<int> laterRuns = [.. Enumerable.Range(link + 1, instructions.Count - link - 1).Where(index => instructions[index].Keyword == Run)];

		Assert.Equal(1, instructions.Count(instruction => instruction.Keyword == Shell));
		Assert.Equal(link + 1, shell);
		Assert.NotEmpty(laterRuns);
		Assert.All(laterRuns, index => Assert.True(index > shell, $"The RUN at instruction {index} comes before the SHELL switch, so Docker would record it as run by /bin/sh and Pyxie could not tell its build cache."));
	}
}
