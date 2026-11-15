using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Where the focus goes on the board after a card's tile retried or closed it, worked out from the cards of the board as it is read again.</summary>
public sealed class BoardFocusResolveTests
{
	[Fact]
	public void InPlaceOf_RemembersThePlaceOfTheCardInItsLaneAndNotInTheBoard()
	{
		List<BoardCard> board = [Shown(1, CardLane.Queue), Shown(2, CardLane.Complete), Shown(3, CardLane.Complete), Shown(4, CardLane.Complete)];

		BoardFocus focus = BoardFocus.InPlaceOf(board[2].Card, board);

		Assert.Equal(new BoardFocus(CardLane.Complete, null, 1), focus);
	}

	[Fact]
	public void InPlaceOf_ACardTheBoardDoesNotShow_TakesTheFirstPlace()
	{
		BoardFocus focus = BoardFocus.InPlaceOf(Shown(9, CardLane.Complete).Card, [Shown(2, CardLane.Complete)]);

		Assert.Equal(0, focus.Index);
	}

	[Fact]
	public void Resolve_ForARetriedCard_FollowsTheCardToTheLaneItIsInNow()
	{
		BoardFocus focus = BoardFocus.OnCard(Shown(5, CardLane.Queue).Card);

		(CardLane lane, int? cardId) = focus.Resolve([Shown(1, CardLane.Queue), Shown(5, CardLane.InProgress)]);

		Assert.Equal((CardLane.InProgress, 5), (lane, cardId));
	}

	[Fact]
	public void Resolve_ForARetriedCardThatIsNoLongerOnTheBoard_FallsBackToTheHeadingOfItsLane()
	{
		BoardFocus focus = BoardFocus.OnCard(Shown(5, CardLane.Queue).Card);

		(CardLane lane, int? cardId) = focus.Resolve([Shown(1, CardLane.Queue)]);

		Assert.Equal((CardLane.Queue, (int?)null), (lane, cardId));
	}

	[Theory]
	[InlineData(0, 11)]
	[InlineData(1, 12)]
	[InlineData(2, 12)]
	public void Resolve_ForAClosedCard_TakesTheCardNowInItsPlaceElseTheLastOne(int index, int expected)
	{
		BoardFocus focus = new(CardLane.Complete, null, index);

		(CardLane lane, int? cardId) = focus.Resolve([Shown(1, CardLane.Queue), Shown(11, CardLane.Complete), Shown(12, CardLane.Complete)]);

		Assert.Equal((CardLane.Complete, expected), (lane, cardId));
	}

	[Fact]
	public void Resolve_ForAClosedCardWhenTheLaneIsEmpty_TakesTheHeading()
	{
		BoardFocus focus = new(CardLane.Complete, null, 0);

		(CardLane lane, int? cardId) = focus.Resolve([Shown(1, CardLane.Queue)]);

		Assert.Equal((CardLane.Complete, (int?)null), (lane, cardId));
	}

	private static BoardCard Shown(int id, CardLane lane)
	{
		WorkCard card = InMemoryCard.Create(id: id);
		card.Lane = lane;

		return new BoardCard(card, CardHistorySummary.From(card.State, []), []);
	}
}
