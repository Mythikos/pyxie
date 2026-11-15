using Pyxie.Data.Enums;
using Pyxie.Data.Services.Rules;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CardLifecycleRulesTests
{
	[Fact]
	public void BoardLanes_AreTheFiveLanesInDisplayOrder()
	{
		Assert.Equal([CardLane.Queue, CardLane.InProgress, CardLane.Gates, CardLane.Review, CardLane.Complete], CardLifecycleRules.BoardLanes);
	}

	[Fact]
	public void BoardLanes_NeverIncludeClosed()
	{
		Assert.DoesNotContain(CardLane.Closed, CardLifecycleRules.BoardLanes);
	}

	[Fact]
	public void BoardLanes_CoverEveryDefinedLaneExceptClosed_SoANewLaneIsNotLeftOffTheBoardUnnoticed()
	{
		IEnumerable<CardLane> expected = Enum.GetValues<CardLane>().Where(lane => lane != CardLane.Closed);

		Assert.Equal(expected.Order(), CardLifecycleRules.BoardLanes.Order());
	}

	[Fact]
	public void BoardLanes_AreAReadOnlyView()
	{
		Assert.True(((ICollection<CardLane>)CardLifecycleRules.BoardLanes).IsReadOnly, "Callers must not be able to change the lanes the board shows.");
	}

	[Theory]
	[InlineData(CardLane.Queue, true)]
	[InlineData(CardLane.InProgress, false)]
	[InlineData(CardLane.Gates, false)]
	[InlineData(CardLane.Review, false)]
	[InlineData(CardLane.Complete, false)]
	[InlineData(CardLane.Closed, false)]
	[InlineData((CardLane)99, false)]
	public void CanEdit_IsTrueOnlyInTheQueue(CardLane lane, bool expected)
	{
		Assert.Equal(expected, CardLifecycleRules.CanEdit(lane));
	}

	[Theory]
	[InlineData(CardLane.Queue, true)]
	[InlineData(CardLane.InProgress, false)]
	[InlineData(CardLane.Gates, false)]
	[InlineData(CardLane.Review, false)]
	[InlineData(CardLane.Complete, false)]
	[InlineData(CardLane.Closed, false)]
	[InlineData((CardLane)99, false)]
	public void CanAdd_IsTrueOnlyInTheEntryLane(CardLane lane, bool expected)
	{
		Assert.Equal(expected, CardLifecycleRules.CanAdd(lane));
	}

	[Theory]
	[InlineData(CardLane.Queue, false)]
	[InlineData(CardLane.InProgress, false)]
	[InlineData(CardLane.Gates, false)]
	[InlineData(CardLane.Review, false)]
	[InlineData(CardLane.Complete, true)]
	[InlineData(CardLane.Closed, false)]
	[InlineData((CardLane)99, false)]
	public void CanClose_IsTrueOnlyInComplete(CardLane lane, bool expected)
	{
		Assert.Equal(expected, CardLifecycleRules.CanClose(lane));
	}

	[Theory]
	[InlineData(CardLane.Queue, CardState.Error, true)]
	[InlineData(CardLane.Queue, CardState.Ready, false)]
	[InlineData(CardLane.InProgress, CardState.Error, false)]
	[InlineData(CardLane.InProgress, CardState.Ready, false)]
	[InlineData(CardLane.Gates, CardState.Error, false)]
	[InlineData(CardLane.Review, CardState.Error, false)]
	[InlineData(CardLane.Complete, CardState.Error, false)]
	[InlineData(CardLane.Closed, CardState.Error, false)]
	[InlineData((CardLane)99, CardState.Error, false)]
	[InlineData(CardLane.Queue, (CardState)99, false)]
	public void CanRetry_IsTrueOnlyForAStoppedCardInTheQueue(CardLane lane, CardState state, bool expected)
	{
		Assert.Equal(expected, CardLifecycleRules.CanRetry(lane, state));
	}

	[Theory]
	[InlineData(CardLane.Queue, false)]
	[InlineData(CardLane.InProgress, false)]
	[InlineData(CardLane.Gates, false)]
	[InlineData(CardLane.Review, false)]
	[InlineData(CardLane.Complete, true)]
	[InlineData(CardLane.Closed, true)]
	[InlineData((CardLane)99, false)]
	public void IsFinished_IsTrueOnlyForCompleteAndClosed(CardLane lane, bool expected)
	{
		Assert.Equal(expected, CardLifecycleRules.IsFinished(lane));
	}

	[Fact]
	public void EntryLane_IsTheQueueAndTheFirstLaneOnTheBoard()
	{
		Assert.Equal(CardLane.Queue, CardLifecycleRules.EntryLane);
		Assert.Equal(CardLifecycleRules.EntryLane, CardLifecycleRules.BoardLanes[0]);
	}
}
