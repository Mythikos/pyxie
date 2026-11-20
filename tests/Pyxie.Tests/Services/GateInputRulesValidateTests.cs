using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="GateInputRules.Validate"/> checks every field of a gate, after trimming, and reports the first that is not valid, in the order the editor shows them: the name, the command, the arguments, then the timeout.</summary>
public sealed class GateInputRulesValidateTests
{
	private static readonly string s_longCommand = new('c', PyxieLimits.MaxCommandLength + 1);
	private static readonly string s_longArguments = new('a', PyxieLimits.MaxArgumentsLength + 1);

	public static IEnumerable<TheoryDataRow<string, string, string, int, string>> FirstProblems { get; } =
	[
		new("", "", s_longArguments, 0, "Gate name is required.") { TestDisplayName = "the name before everything else" },
		new("Tests", s_longCommand, s_longArguments, 0, "Command must be 500 characters or fewer.") { TestDisplayName = "the command before the arguments" },
		new("Tests", "dotnet", s_longArguments, 0, "Arguments must be 2000 characters or fewer.") { TestDisplayName = "the arguments before the timeout" },
		new("Tests", "dotnet", "test", 0, "Timeout must be between 1 and 240 minutes.") { TestDisplayName = "the timeout last" },
	];

	[Theory]
	[InlineData("Tests", "dotnet", "test --no-build", 1)]
	[InlineData("  Tests  ", "  dotnet  ", "", PyxieLimits.MaxGateTimeoutMinutes)]
	public void Validate_OfAValidGate_FindsNothing(string name, string command, string arguments, int timeoutMinutes)
	{
		Assert.Null(GateInputRules.Validate(new GateInput(name, command, arguments, timeoutMinutes, IsEnabled: false)));
	}

	[Theory]
	[MemberData(nameof(FirstProblems))]
	public void Validate_OfSeveralProblems_ReportsTheFirstInTheEditorsOrder(string name, string command, string arguments, int timeoutMinutes, string expected)
	{
		Assert.Equal(expected, GateInputRules.Validate(new GateInput(name, command, arguments, timeoutMinutes, IsEnabled: true)));
	}

	[Fact]
	public void Validate_ChecksTheNameAndCommandAfterTrimming()
	{
		Assert.Equal("Gate name is required.", GateInputRules.Validate(new GateInput(" \t ", "dotnet", "test", 30, IsEnabled: true)));
		Assert.Equal("Command is required.", GateInputRules.Validate(new GateInput("Tests", "\r\n", "test", 30, IsEnabled: true)));
		Assert.Null(GateInputRules.Validate(new GateInput($" {new string('n', PyxieLimits.MaxNameLength)} ", $" {new string('c', PyxieLimits.MaxCommandLength)} ", "test", 30, IsEnabled: true)));
	}
}
