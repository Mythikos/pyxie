using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>Builds process requests that run the fake harness, the only program tests start (see <see cref="FakeHarnessAgent"/> for its arguments).</summary>
public static class FakeHarnessRequest
{
	/// <summary>Creates a request that runs <c>dotnet Pyxie.FakeHarness.dll</c> with behavior arguments. Its timeout is two minutes, so a hung process fails the test instead of the default ten minutes.</summary>
	/// <param name="workingDirectory">The folder to run in.</param>
	/// <param name="fakeArguments">The behavior arguments, each one argument.</param>
	/// <returns>The request.</returns>
	public static ProcessRequest Create(string workingDirectory, params string[] fakeArguments)
	{
		return new ProcessRequest(FakeHarnessAgent.Command, [FakeHarnessAgent.DllPath, .. fakeArguments], workingDirectory) { Timeout = TimeSpan.FromMinutes(2) };
	}
}
