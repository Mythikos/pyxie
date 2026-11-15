using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> and environment variables, read back by a batch file that prints them with <c>set</c>: the request's variables are set on top of Pyxie's own, and replace a variable of the same name. Neither the fake harness nor any other program here prints its environment, so these tests need Windows.</summary>
public sealed class ProcessRunnerEnvironmentTests
{
	private const string FirstVariable = "PYXIE_PROC_FIRST";
	private const string SecondVariable = "PYXIE_PROC_SECOND";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_SetsTheRequestedVariablesForTheProcess()
	{
		await using ScratchFolder scratch = new();
		Dictionary<string, string> environment = new() { [FirstVariable] = "one", [SecondVariable] = "two words & (more) | less" };

		Dictionary<string, string> seen = await PrintEnvironmentAsync(scratch, environment);

		Assert.Equal("one", seen[FirstVariable]);
		Assert.Equal("two words & (more) | less", seen[SecondVariable]);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_KeepsTheVariablesPyxieItselfHas()
	{
		await using ScratchFolder scratch = new();

		Dictionary<string, string> seen = await PrintEnvironmentAsync(scratch, new Dictionary<string, string> { [FirstVariable] = "one" });

		Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), seen["SystemRoot"], ignoreCase: true);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_ARequestedVariableReplacesOneOfTheSameName()
	{
		await using ScratchFolder scratch = new();
		string replacement = scratch.Combine("replacement-temp");

		Dictionary<string, string> seen = await PrintEnvironmentAsync(scratch, new Dictionary<string, string> { ["TEMP"] = replacement });

		Assert.Equal(replacement, seen["TEMP"]);
	}

	// Runs a batch file that prints the variables the tests look at, each as NAME=value, and reads them back.
	private static async Task<Dictionary<string, string>> PrintEnvironmentAsync(ScratchFolder scratch, IReadOnlyDictionary<string, string> environment)
	{
		string script = await BatchScript.WriteAsync(scratch.Folder, "environment.cmd", $"@set {FirstVariable}", $"@set {SecondVariable}", "@set TEMP", "@set SystemRoot");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(script, [], scratch.Folder) { Environment = environment }, Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The batch file did not succeed: {result.Output}");
		Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (string line in result.Output.ReplaceLineEndings("\n").Split('\n'))
		{
			int equals = line.IndexOf('=', StringComparison.Ordinal);
			if (equals > 0)
			{
				seen[line[..equals]] = line[(equals + 1)..];
			}
		}

		return seen;
	}
}
