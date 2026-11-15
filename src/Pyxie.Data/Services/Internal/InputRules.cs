namespace Pyxie.Data.Services.Internal;

/// <summary>Shared normalization and validation rules for text supplied to services.</summary>
internal static class InputRules
{
	/// <summary>Trims a value, treating <see langword="null"/> as empty.</summary>
	/// <param name="value">The raw value.</param>
	/// <returns>The trimmed value, never <see langword="null"/>.</returns>
	public static string Normalize(string? value)
	{
		return value?.Trim() ?? string.Empty;
	}

	/// <summary>Checks that a normalized value is present and within its length limit.</summary>
	/// <param name="value">The normalized value.</param>
	/// <param name="label">The user-facing name of the field.</param>
	/// <param name="maxLength">The maximum allowed length.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the value is valid.</returns>
	public static string? ValidateRequired(string value, string label, int maxLength)
	{
		if (value.Length == 0)
		{
			return $"{label} is required.";
		}

		return ValidateMaxLength(value, label, maxLength);
	}

	/// <summary>Checks that a normalized value is within its length limit.</summary>
	/// <param name="value">The normalized value.</param>
	/// <param name="label">The user-facing name of the field.</param>
	/// <param name="maxLength">The maximum allowed length.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the value is valid.</returns>
	public static string? ValidateMaxLength(string value, string label, int maxLength)
	{
		return value.Length > maxLength ? $"{label} must be {maxLength} characters or fewer." : null;
	}

	/// <summary>Determines whether any existing name equals a candidate, ignoring case.</summary>
	/// <param name="existingNames">The names already in use.</param>
	/// <param name="name">The candidate name.</param>
	/// <returns><see langword="true"/> when the candidate duplicates an existing name.</returns>
	public static bool ContainsName(IEnumerable<string> existingNames, string name)
	{
		return existingNames.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
	}
}
