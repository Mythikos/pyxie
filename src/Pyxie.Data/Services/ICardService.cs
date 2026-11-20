using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>The operations people use on cards. <see cref="Rules.CardLifecycleRules"/> decides which changes people may make in which lane and state; the service refuses the others. Cards move between lanes only through the pipeline (<see cref="Pipeline.ICardPipelineService"/>), never through this service.</summary>
public interface ICardService
{
	/// <summary>Gets every card on the board with its category, assigned agents, and history summary, ordered by lane then sort order. Closed cards are history and are not included.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The cards on the board.</returns>
	Task<IReadOnlyList<BoardCard>> GetBoardAsync(CancellationToken cancellationToken);

	/// <summary>Reads a page of active board cards in stable card ID order, with history counts.</summary>
	/// <param name="afterId">The last card ID already read, or zero to start.</param>
	/// <param name="count">Maximum number of cards to return.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The next cards and their board summaries.</returns>
	Task<IReadOnlyList<BoardCardSummary>> GetBoardPageAsync(int afterId, int count, CancellationToken cancellationToken);

	/// <summary>Gets a card's rounds in order, with their agents, briefings, notes, and reports. Works for cards in every lane, closed ones included.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The rounds, oldest first, or a failure when the card does not exist.</returns>
	Task<ServiceResult<IReadOnlyList<CardRound>>> GetHistoryAsync(int id, CancellationToken cancellationToken);

	/// <summary>Reads a bounded page of round reports starting at a one-based sequence number.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="firstSequence">First sequence number to include.</param>
	/// <param name="count">Maximum number of rounds to load.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The matching rounds in sequence order, or a failure for an unknown card.</returns>
	Task<ServiceResult<IReadOnlyList<CardRound>>> GetHistoryPageAsync(int id, int firstSequence, int count, CancellationToken cancellationToken);

	/// <summary>Gets one card with its category and agents, whatever lane it is in. Unlike the board, this includes closed cards.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The card, or a failure when it does not exist.</returns>
	Task<ServiceResult<WorkCard>> GetAsync(int id, CancellationToken cancellationToken);

	/// <summary>Creates a card at the bottom of the Queue lane.</summary>
	/// <param name="input">The card fields. Text is trimmed; name, description, an existing category, and a worker that can work are required, and the attempt limit must be in range.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The created card with its category and agents, or a failure naming the first blank, over-long, missing, unknown, or out-of-range field.</returns>
	Task<ServiceResult<WorkCard>> CreateAsync(CardInput input, CancellationToken cancellationToken);

	/// <summary>Updates the editable fields of a card that is still in the Queue. The same rules apply as for creation. Editing a card the pipeline stopped keeps it stopped, and its next run starts at work, because the task it stopped on may have changed.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="input">The new card fields, with the same rules as for creation.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The updated card with its category and agents, or a failure when the card does not exist, has been picked up and can no longer be edited, or the fields are rejected. A card that is picked up while the change is being saved is refused as well and left untouched.</returns>
	Task<ServiceResult<WorkCard>> UpdateAsync(int id, CardInput input, CancellationToken cancellationToken);

	/// <summary>Deletes a card that is still in the Queue.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <param name="expectedVersion">The version the caller inspected, or null to use the version at the start of the operation.</param>
	/// <returns>A failure when the card no longer exists, has changed, or has left the Queue.</returns>
	Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken, int? expectedVersion = null);

	/// <summary>Makes a card the pipeline stopped Ready again, with a fresh set of attempts. It keeps its place at the top of the Queue, so the pipeline takes it next and resumes it at the stage it stopped at (see <see cref="Entities.WorkCard.ResumeRound"/>).</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the card does not exist, is not stopped, or changed while it was being retried.</returns>
	Task<ServiceResult> RetryAsync(int id, CancellationToken cancellationToken);

	/// <summary>Closes out a completed card: it leaves the board and stays in the database as history, stamped with the closing time. The remaining Complete cards keep their order.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the card does not exist, is already closed, or is not in the Complete lane.</returns>
	Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken);

	/// <summary>Closes every card currently in Complete in one write, preserving history.</summary>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>The number of cards closed.</returns>
	Task<int> CloseCompletedAsync(CancellationToken cancellationToken);

	/// <summary>Gets a page of completions by successful merge time, including cards closed later.</summary>
	/// <param name="fromUtc">First instant to include.</param>
	/// <param name="untilUtc">Exclusive upper bound.</param>
	/// <param name="offset">Number of earlier results to skip.</param>
	/// <param name="count">Maximum number of results.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The matching cards, newest merge first.</returns>
	Task<IReadOnlyList<CompletedCard>> GetCompletedAsync(DateTime fromUtc, DateTime untilUtc, int offset, int count, CancellationToken cancellationToken);
}
