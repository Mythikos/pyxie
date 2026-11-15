using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;

namespace Pyxie.Web.Components.Board;

/// <summary>Where the keyboard focus goes on the board after a person retried or closed a card from its tile and the board was read again. The button they pressed went with the change, and the focus should not fall to the page. A retried card stays on the board, so the focus follows it. A closed card leaves it, so the focus goes to the tile that took its place in the lane (the next card, else the previous one), or to the lane's heading when the lane is now empty.</summary>
/// <param name="Lane">The lane the card was in, whose heading takes the focus when there is no card to.</param>
/// <param name="CardId">The card whose name button takes the focus wherever the card is now, or <see langword="null"/> to take the card now at <paramref name="Index"/> in the lane.</param>
/// <param name="Index">The place in the lane of the card that left it.</param>
public sealed record BoardFocus(CardLane Lane, int? CardId, int Index)
{
	/// <summary>Creates the focus that follows a card, for a card that stays on the board.</summary>
	/// <param name="card">The card.</param>
	/// <returns>The focus.</returns>
	public static BoardFocus OnCard(WorkCard card)
	{
		return new BoardFocus(card.Lane, card.Id, 0);
	}

	/// <summary>Creates the focus that takes the place of a card that is about to leave its lane.</summary>
	/// <param name="card">The card that will leave.</param>
	/// <param name="cards">The cards of the board as they are shown now, ordered by lane and then by place in it.</param>
	/// <returns>The focus.</returns>
	public static BoardFocus InPlaceOf(WorkCard card, IEnumerable<BoardCard> cards)
	{
		List<BoardCard> lane = [.. cards.Where(shown => shown.Card.Lane == card.Lane)];

		return new BoardFocus(card.Lane, null, Math.Max(lane.FindIndex(shown => shown.Card.Id == card.Id), 0));
	}

	/// <summary>Finds where the focus goes on the board as it is now.</summary>
	/// <param name="cards">The cards of the board after it was read again, ordered by lane and then by place in it.</param>
	/// <returns>The lane that takes the focus, and the card in it whose name button takes it, or <see langword="null"/> for the lane's heading.</returns>
	public (CardLane Lane, int? CardId) Resolve(IEnumerable<BoardCard> cards)
	{
		if (CardId is int id)
		{
			BoardCard? followed = cards.FirstOrDefault(shown => shown.Card.Id == id);

			return followed is null ? (Lane, null) : (followed.Card.Lane, id);
		}

		List<BoardCard> lane = [.. cards.Where(shown => shown.Card.Lane == Lane)];

		return (Lane, lane.Count == 0 ? null : lane[Math.Min(Index, lane.Count - 1)].Card.Id);
	}
}
