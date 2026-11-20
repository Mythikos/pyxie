using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Pipeline;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The rules for an <see cref="AgentInput"/>, shared by the agent service, the settings import, and the agent editor so all of them report the same messages.</summary>
public static class AgentInputRules
{
	/// <summary>Gets an agent input as it is checked and stored: its name, command, and arguments trimmed.</summary>
	/// <param name="input">The input as entered.</param>
	/// <returns>The normalized input.</returns>
	public static AgentInput Normalize(AgentInput input)
	{
		return input with { Name = InputRules.Normalize(input.Name), Command = InputRules.Normalize(input.Command), Arguments = InputRules.Normalize(input.Arguments) };
	}

	/// <summary>Checks an agent input's fields as they will be stored. Whether another agent already has the name, or the merge role, is decided against the stored agents.</summary>
	/// <param name="input">The input.</param>
	/// <returns>A user-presentable message for the first field that is not valid, or <see langword="null"/> when all are.</returns>
	public static string? Validate(AgentInput input)
	{
		if (!Enum.IsDefined(input.Harness))
		{
			return "Harness is not recognized.";
		}

		bool supported = input.Harness == HarnessKind.Other
			? input.Authentication == AgentAuthentication.None
			: input.Authentication is AgentAuthentication.Account or AgentAuthentication.ApiKey;
		if (!supported)
		{
			return "Choose an authentication method supported by this harness.";
		}

		return InputRules.ValidateRequired(InputRules.Normalize(input.Name), "Agent name", PyxieLimits.MaxNameLength) ?? InputRules.ValidateMaxLength(InputRules.Normalize(input.Command), "Command", PyxieLimits.MaxCommandLength) ?? ValidateArguments(input.Arguments);
	}

	/// <summary>Checks agent arguments after trimming them. Arguments are optional; they must fit the limit and may carry only the tokens of <see cref="AgentArgumentTokens"/>.</summary>
	/// <param name="arguments">The raw arguments.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the arguments are valid or empty.</returns>
	public static string? ValidateArguments(string? arguments)
	{
		string normalized = InputRules.Normalize(arguments);

		return InputRules.ValidateMaxLength(normalized, "Arguments", PyxieLimits.MaxArgumentsLength) ?? AgentArgumentTokens.Validate(normalized);
	}
}
