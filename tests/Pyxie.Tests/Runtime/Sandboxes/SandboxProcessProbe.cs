using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>Looks for a program the sandbox's user still has running. A sandbox has no init process to reap what was orphaned, so a process that was ended lingers as a zombie; it runs nothing, so only processes that are running, sleeping, waiting on disk, or stopped count.</summary>
public static class SandboxProcessProbe
{
	/// <summary>Gets whether the sandbox's user has a live process of a program.</summary>
	/// <param name="sandbox">The sandbox.</param>
	/// <param name="program">The program's name, matched exactly.</param>
	/// <returns><see langword="true"/> when one is alive.</returns>
	public static async Task<bool> IsRunningAsync(ISandbox sandbox, string program)
	{
		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("pgrep", ["--runstates", "R,S,D,T", "-u", "node", "-x", program], sandbox.WorkPath), TestContext.Current.CancellationToken);
		Assert.True(result.Outcome == ProcessOutcome.Exited && result.ExitCode is 0 or 1, $"pgrep could not look: {result.Outcome}, exit code {result.ExitCode}: {result.Output}");

		return result.ExitCode == 0;
	}
}
