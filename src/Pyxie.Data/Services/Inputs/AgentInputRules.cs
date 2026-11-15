using Pyxie.Data.Limits;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Pipeline;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The rules for an <see cref="AgentInput"/>'s arguments, shared by the agent service and the agent editor so both report the same messages.</summary>
public static class AgentInputRules
{
	/// <summary>Checks agent arguments after trimming them. Arguments are optional; they must fit the limit and may carry only the tokens of <see cref="AgentArgumentTokens"/>.</summary>
	/// <param name="arguments">The raw arguments.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the arguments are valid or empty.</returns>
	public static string? ValidateArguments(string? arguments)
	{
		string normalized = InputRules.Normalize(arguments);

		return InputRules.ValidateMaxLength(normalized, "Arguments", PyxieLimits.MaxArgumentsLength) ?? AgentArgumentTokens.Validate(normalized);
	}
}
