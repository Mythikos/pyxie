using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Tests.Support;

/// <summary>Builds valid <see cref="GateInput"/> values so a test names only the field it is about.</summary>
public static class GateTestInput
{
	/// <summary>Creates gate input that passes validation unless an argument says otherwise.</summary>
	/// <param name="name">The gate name.</param>
	/// <param name="command">The executable; <c>dotnet</c> unless given.</param>
	/// <param name="arguments">The arguments; <c>test</c> unless given.</param>
	/// <param name="timeoutMinutes">The timeout; the default gate timeout unless given.</param>
	/// <param name="isEnabled">Whether the gate is enabled; enabled unless given.</param>
	/// <returns>The gate input.</returns>
	public static GateInput Create(string name = "Tests", string command = "dotnet", string arguments = "test", int timeoutMinutes = PyxieLimits.DefaultGateTimeoutMinutes, bool isEnabled = true)
	{
		return new GateInput(name, command, arguments, timeoutMinutes, isEnabled);
	}
}
