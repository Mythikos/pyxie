using Pyxie.Data.Limits;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The field rules for a <see cref="CardInput"/>, shared by the card service and the card editor so both report the same messages.</summary>
public static class CardInputRules
{
	/// <summary>Checks a card name after trimming it.</summary>
	/// <param name="name">The raw name.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the name is valid.</returns>
	public static string? ValidateName(string? name)
	{
		return InputRules.ValidateRequired(InputRules.Normalize(name), "Name", PyxieLimits.MaxNameLength);
	}

	/// <summary>Checks a card description after trimming it.</summary>
	/// <param name="description">The raw description.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the description is valid.</returns>
	public static string? ValidateDescription(string? description)
	{
		return InputRules.ValidateRequired(InputRules.Normalize(description), "Description", PyxieLimits.MaxDescriptionLength);
	}

	/// <summary>Checks that a category was chosen. Whether it exists is decided by the card service.</summary>
	/// <param name="categoryId">The chosen category identifier, or <see langword="null"/> when none was chosen.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when a category was chosen.</returns>
	public static string? ValidateCategory(int? categoryId)
	{
		return categoryId is > 0 ? null : "Category is required.";
	}

	/// <summary>Checks that a worker was chosen. Whether it exists and may work is decided by the card service.</summary>
	/// <param name="workerAgentId">The chosen worker identifier, or <see langword="null"/> when none was chosen.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when a worker was chosen.</returns>
	public static string? ValidateWorker(int? workerAgentId)
	{
		return workerAgentId is > 0 ? null : "Worker is required.";
	}

	/// <summary>Checks a card's attempt limit.</summary>
	/// <param name="attemptLimit">The attempt limit.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the limit is between 1 and <see cref="PyxieLimits.MaxAttemptLimit"/>.</returns>
	public static string? ValidateAttemptLimit(int attemptLimit)
	{
		return attemptLimit is >= 1 and <= PyxieLimits.MaxAttemptLimit ? null : $"Attempts must be between 1 and {PyxieLimits.MaxAttemptLimit}.";
	}
}
