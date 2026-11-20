using Pyxie.Data.Limits;
using Pyxie.Data.Media;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The editable fields of a card. Name, description, category, and worker are required; the reviewer is optional.</summary>
/// <param name="Name">The card title; must not be blank.</param>
/// <param name="Description">The description; must not be blank.</param>
/// <param name="CategoryId">The identifier of an existing category; a value that is not positive means no category was chosen.</param>
/// <param name="WorkerAgentId">The worker agent; <see langword="null"/> means none was chosen, which is refused.</param>
/// <param name="ReviewerAgentId">The reviewer agent, or <see langword="null"/> to skip review: the card goes from the gates straight to the merge.</param>
/// <param name="AttemptLimit">How many attempts each run of the card gets, from 1 to <see cref="PyxieLimits.MaxAttemptLimit"/>.</param>
public sealed record CardInput(
	string Name,
	string Description,
	int CategoryId,
	int? WorkerAgentId,
	int? ReviewerAgentId,
	int AttemptLimit = PyxieLimits.DefaultAttemptLimit)
{
	/// <summary>Gets the complete context selection, or null to preserve the existing selection.</summary>
	public MediaEdit? Media { get; init; }

	/// <summary>Gets the version the editor opened on, or null to check the version read when the operation starts.</summary>
	public int? ExpectedVersion { get; init; }
}
