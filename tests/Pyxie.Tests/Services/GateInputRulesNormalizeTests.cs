using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="GateInputRules.Normalize"/> gives a gate as it is checked and stored: its name, command, and arguments trimmed, with the whitespace inside them, the timeout, and whether it runs kept.</summary>
public sealed class GateInputRulesNormalizeTests
{
	[Theory]
	[InlineData(1, true)]
	[InlineData(45, false)]
	public void Normalize_TrimsTheNameCommandAndArguments_AndKeepsTheTimeoutAndWhetherItRuns(int timeoutMinutes, bool isEnabled)
	{
		GateInput input = new(" Unit  tests\t", "\r\n dotnet ", "  test  --no-build  ", timeoutMinutes, isEnabled);

		GateInput normalized = GateInputRules.Normalize(input);

		Assert.Equal(new GateInput("Unit  tests", "dotnet", "test  --no-build", timeoutMinutes, isEnabled), normalized);
	}

	[Fact]
	public void Normalize_OfANormalizedGate_ChangesNothing()
	{
		GateInput input = new("Build", "dotnet", "build", 30, IsEnabled: true);

		Assert.Equal(input, GateInputRules.Normalize(input));
	}
}
