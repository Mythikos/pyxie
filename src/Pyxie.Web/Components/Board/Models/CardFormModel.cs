using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Web.Components.Board.Models;

/// <summary>The mutable state of the card dialog's form, with the messages for fields that cannot be saved as they are.</summary>
public sealed class CardFormModel
{
	/// <summary>The identifier the selects use for "None": an item whose value is null is not shown as the selected option, but 0 is never an identifier.</summary>
	public const int NoSelection = 0;

	/// <summary>Gets or sets the card name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the card description.</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>Gets or sets the chosen category, or <see cref="NoSelection"/>.</summary>
	public int CategoryId { get; set; } = NoSelection;

	/// <summary>Gets or sets the chosen worker, or <see cref="NoSelection"/>.</summary>
	public int WorkerAgentId { get; set; } = NoSelection;

	/// <summary>Gets or sets the chosen reviewer, or <see cref="NoSelection"/>.</summary>
	public int ReviewerAgentId { get; set; } = NoSelection;

	/// <summary>Gets or sets how many attempts each run of the card gets.</summary>
	public int AttemptLimit { get; set; } = PyxieLimits.DefaultAttemptLimit;

	/// <summary>Gets the message for a name that cannot be saved, or <see langword="null"/>.</summary>
	public string? NameError => CardInputRules.ValidateName(Name);

	/// <summary>Gets the message for a description that cannot be saved, or <see langword="null"/>.</summary>
	public string? DescriptionError => CardInputRules.ValidateDescription(Description);

	/// <summary>Gets the message for a missing category, or <see langword="null"/>.</summary>
	public string? CategoryError => CardInputRules.ValidateCategory(CategoryId);

	/// <summary>Gets the message for a missing worker, or <see langword="null"/>.</summary>
	public string? WorkerError => CardInputRules.ValidateWorker(ToAssignment(WorkerAgentId));

	/// <summary>Gets the message for an attempt limit out of range, or <see langword="null"/>.</summary>
	public string? AttemptLimitError => CardInputRules.ValidateAttemptLimit(AttemptLimit);

	/// <summary>Gets a value indicating whether every field can be saved.</summary>
	public bool IsValid => NameError is null && DescriptionError is null && CategoryError is null && WorkerError is null && AttemptLimitError is null;

	/// <summary>Creates the form state for a new card: nothing chosen, and the default number of attempts.</summary>
	/// <returns>A new form model.</returns>
	public static CardFormModel CreateNew()
	{
		return new CardFormModel();
	}

	/// <summary>Creates the form state for a stored card.</summary>
	/// <param name="card">The card to edit.</param>
	/// <returns>A form model holding the card's values.</returns>
	public static CardFormModel FromCard(WorkCard card)
	{
		return new CardFormModel
		{
			Name = card.Name,
			Description = card.Description,
			CategoryId = card.CategoryId,
			WorkerAgentId = card.WorkerAgentId ?? NoSelection,
			ReviewerAgentId = card.ReviewerAgentId ?? NoSelection,
			AttemptLimit = card.AttemptLimit,
		};
	}

	/// <summary>Converts the form state into a service input with trimmed text.</summary>
	/// <returns>The card input.</returns>
	public CardInput ToInput()
	{
		return new CardInput(Name.Trim(), Description.Trim(), CategoryId, ToAssignment(WorkerAgentId), ToAssignment(ReviewerAgentId), AttemptLimit);
	}

	/// <summary>Determines whether saving the form would change a stored card.</summary>
	/// <param name="card">The stored card.</param>
	/// <returns><see langword="true"/> when any field differs from the card's, ignoring the whitespace around text.</returns>
	public bool DiffersFrom(WorkCard card)
	{
		return Name.Trim() != card.Name || Description.Trim() != card.Description || CategoryId != card.CategoryId || ToAssignment(WorkerAgentId) != card.WorkerAgentId || ToAssignment(ReviewerAgentId) != card.ReviewerAgentId || AttemptLimit != card.AttemptLimit;
	}

	private static int? ToAssignment(int agentId)
	{
		return agentId == NoSelection ? null : agentId;
	}
}
