using Pyxie.Data.Limits;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The field rules for a <see cref="GateInput"/>, shared by the gate service and the gate editor so both report the same messages.</summary>
public static class GateInputRules
{
	/// <summary>Checks a gate name after trimming it. Whether another gate already has the name is decided by the gate service.</summary>
	/// <param name="name">The raw name.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the name is valid.</returns>
	public static string? ValidateName(string? name)
	{
		return InputRules.ValidateRequired(InputRules.Normalize(name), "Gate name", PyxieLimits.MaxNameLength);
	}

	/// <summary>Checks a gate command after trimming it.</summary>
	/// <param name="command">The raw command.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the command is valid.</returns>
	public static string? ValidateCommand(string? command)
	{
		return InputRules.ValidateRequired(InputRules.Normalize(command), "Command", PyxieLimits.MaxCommandLength);
	}

	/// <summary>Checks gate arguments after trimming them. Arguments are optional.</summary>
	/// <param name="arguments">The raw arguments.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the arguments are valid or empty.</returns>
	public static string? ValidateArguments(string? arguments)
	{
		return InputRules.ValidateMaxLength(InputRules.Normalize(arguments), "Arguments", PyxieLimits.MaxArgumentsLength);
	}

	/// <summary>Checks a gate timeout.</summary>
	/// <param name="timeoutMinutes">The timeout in minutes.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the timeout is between 1 and <see cref="PyxieLimits.MaxGateTimeoutMinutes"/> minutes.</returns>
	public static string? ValidateTimeout(int timeoutMinutes)
	{
		return timeoutMinutes is >= 1 and <= PyxieLimits.MaxGateTimeoutMinutes ? null : $"Timeout must be between 1 and {PyxieLimits.MaxGateTimeoutMinutes} minutes.";
	}
}
