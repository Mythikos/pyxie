using Pyxie.Data.Limits;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The rules for a <see cref="CategoryInput"/>, shared by the category service and the settings import so both report the same messages. Every <see cref="Colors.HexColor"/> is a valid color, so only the name has rules.</summary>
public static class CategoryInputRules
{
	/// <summary>Gets a category input as it is checked and stored: its name trimmed.</summary>
	/// <param name="input">The input as entered.</param>
	/// <returns>The normalized input.</returns>
	public static CategoryInput Normalize(CategoryInput input)
	{
		return input with { Name = InputRules.Normalize(input.Name) };
	}

	/// <summary>Checks a category name after trimming it. Whether another category already has the name is decided against the stored categories.</summary>
	/// <param name="name">The raw name.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the name is valid.</returns>
	public static string? ValidateName(string? name)
	{
		return InputRules.ValidateRequired(InputRules.Normalize(name), "Category name", PyxieLimits.MaxNameLength);
	}
}
