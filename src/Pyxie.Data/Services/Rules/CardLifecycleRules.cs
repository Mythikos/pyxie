using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Rules;

/// <summary>Owns what people may do with a card in each lane. The pipeline advances cards (see <see cref="PipelineRules"/> and <see cref="Pipeline.ICardPipelineService"/>); people change, delete, or retry a card only while it waits in the Queue, and close it out once it is Complete. The card service enforces these rules and the user interface reads them to decide what to offer, so neither keeps its own copy.</summary>
public static class CardLifecycleRules
{
	/// <summary>Gets the lanes shown on the board, in display order. <see cref="CardLane.Closed"/> is not a board lane.</summary>
	public static IReadOnlyList<CardLane> BoardLanes { get; } = Array.AsReadOnly<CardLane>([CardLane.Queue, CardLane.InProgress, CardLane.Gates, CardLane.Review, CardLane.Complete]);

	/// <summary>Gets the lanes of finished cards: merged, and perhaps closed out. Agents assigned to unfinished cards must stay assigned.</summary>
	public static IReadOnlyList<CardLane> FinishedLanes { get; } = Array.AsReadOnly<CardLane>([CardLane.Complete, CardLane.Closed]);

	/// <summary>Gets the lane new cards join, at the bottom, and the lane the pipeline takes new work from, at the top. Cards the pipeline stops wait here too.</summary>
	public static CardLane EntryLane => CardLane.Queue;

	/// <summary>Determines whether people may add a card in a lane. New cards enter the <see cref="EntryLane"/> and nowhere else.</summary>
	/// <param name="lane">The lane.</param>
	/// <returns><see langword="true"/> for the <see cref="EntryLane"/> only.</returns>
	public static bool CanAdd(CardLane lane)
	{
		return lane == EntryLane;
	}

	/// <summary>Determines whether people may edit or delete a card in a lane. Once the pipeline has claimed a card they may only review it.</summary>
	/// <param name="lane">The lane the card is in.</param>
	/// <returns><see langword="true"/> for the Queue only.</returns>
	public static bool CanEdit(CardLane lane)
	{
		return lane == CardLane.Queue;
	}

	/// <summary>Determines whether people may retry a card: one the pipeline stopped in Error, which waits in the Queue.</summary>
	/// <param name="lane">The lane the card is in.</param>
	/// <param name="state">The card's state.</param>
	/// <returns><see langword="true"/> for a Queue card in <see cref="CardState.Error"/>.</returns>
	public static bool CanRetry(CardLane lane, CardState state)
	{
		return lane == CardLane.Queue && state == CardState.Error;
	}

	/// <summary>Determines whether people may close out a card in a lane, which removes it from the board and keeps it as history.</summary>
	/// <param name="lane">The lane the card is in.</param>
	/// <returns><see langword="true"/> for Complete only.</returns>
	public static bool CanClose(CardLane lane)
	{
		return lane == CardLane.Complete;
	}

	/// <summary>Determines whether a card's work is finished (see <see cref="FinishedLanes"/>).</summary>
	/// <param name="lane">The lane the card is in.</param>
	/// <returns><see langword="true"/> for Complete and Closed.</returns>
	public static bool IsFinished(CardLane lane)
	{
		return FinishedLanes.Contains(lane);
	}
}
