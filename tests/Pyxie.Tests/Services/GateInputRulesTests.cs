using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class GateInputRulesTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateName_RejectsMissingOrBlankName(string? name)
	{
		Assert.Equal("Gate name is required.", GateInputRules.ValidateName(name));
	}

	[Fact]
	public void ValidateName_AcceptsNameAtLimitAfterTrimming_AndRejectsOneOver()
	{
		Assert.Null(GateInputRules.ValidateName($"  {new string('n', PyxieLimits.MaxNameLength)}  "));
		Assert.Equal("Gate name must be 100 characters or fewer.", GateInputRules.ValidateName(new string('n', PyxieLimits.MaxNameLength + 1)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateCommand_RejectsMissingOrBlankCommand(string? command)
	{
		Assert.Equal("Command is required.", GateInputRules.ValidateCommand(command));
	}

	[Fact]
	public void ValidateCommand_AcceptsCommandAtLimitAfterTrimming_AndRejectsOneOver()
	{
		Assert.Null(GateInputRules.ValidateCommand($"  {new string('c', PyxieLimits.MaxCommandLength)}  "));
		Assert.Equal("Command must be 500 characters or fewer.", GateInputRules.ValidateCommand(new string('c', PyxieLimits.MaxCommandLength + 1)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("test --no-build")]
	public void ValidateArguments_AcceptsMissingBlankAndOrdinaryArguments(string? arguments)
	{
		Assert.Null(GateInputRules.ValidateArguments(arguments));
	}

	[Fact]
	public void ValidateArguments_AcceptsArgumentsAtLimitAfterTrimming_AndRejectsOneOver()
	{
		Assert.Null(GateInputRules.ValidateArguments($"  {new string('a', PyxieLimits.MaxArgumentsLength)}  "));
		Assert.Equal("Arguments must be 2000 characters or fewer.", GateInputRules.ValidateArguments(new string('a', PyxieLimits.MaxArgumentsLength + 1)));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(PyxieLimits.DefaultGateTimeoutMinutes)]
	[InlineData(PyxieLimits.MaxGateTimeoutMinutes)]
	public void ValidateTimeout_AcceptsOneMinuteUpToTheMaximum(int timeoutMinutes)
	{
		Assert.Null(GateInputRules.ValidateTimeout(timeoutMinutes));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(PyxieLimits.MaxGateTimeoutMinutes + 1)]
	[InlineData(int.MaxValue)]
	[InlineData(int.MinValue)]
	public void ValidateTimeout_RejectsEverythingOutsideThatRangeAndNamesIt(int timeoutMinutes)
	{
		Assert.Equal("Timeout must be between 1 and 240 minutes.", GateInputRules.ValidateTimeout(timeoutMinutes));
	}
}
