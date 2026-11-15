using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.Internal;

/// <summary>Keeps the sort order of cards within a lane contiguous.</summary>
internal static class LaneSequencer
{
	/// <summary>Assigns consecutive sort orders following the order of the list.</summary>
	/// <param name="laneCards">The cards of one lane in display order.</param>
	/// <param name="firstPosition">The sort order of the first card; 1 leaves position 0 for a card placed at the top.</param>
	public static void Resequence(IReadOnlyList<WorkCard> laneCards, int firstPosition = 0)
	{
		for (int position = 0; position < laneCards.Count; position++)
		{
			laneCards[position].SortOrder = firstPosition + position;
		}
	}
}
