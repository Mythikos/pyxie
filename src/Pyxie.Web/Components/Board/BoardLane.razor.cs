using System.Collections.ObjectModel;

using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;

namespace Pyxie.Web.Components.Board;

/// <summary>A board lane: a header with its colored dot, name, and card count above its cards. The keyboard focus can be sent to one of its cards or, when the card is gone, to its heading, so it stays in the lane after a tile goes away.</summary>
public partial class BoardLane : ComponentBase
{
	// The tiles by card, to focus the one a person's focus moves to when the tile it was on leaves the lane.
	private readonly Dictionary<int, CardTile> _tiles = [];

	// The heading is where the focus goes when the lane has no card to take it.
	private ElementReference _heading;

	/// <summary>Gets or sets the lane this component represents.</summary>
	[Parameter]
	public CardLane Lane { get; set; }

	/// <summary>Gets or sets the cards in the lane, in display order.</summary>
	[Parameter]
	public IReadOnlyList<BoardCard> Cards { get; set; } = [];

	/// <summary>Gets or sets the callback raised with the card when one of its tiles is clicked.</summary>
	[Parameter]
	public EventCallback<WorkCard> CardClicked { get; set; }

	/// <summary>Gets or sets the callback raised when the add-card action is chosen.</summary>
	[Parameter]
	public EventCallback AddClicked { get; set; }

	/// <summary>Gets or sets the callback raised with the card when one of its tiles asks to retry it.</summary>
	[Parameter]
	public EventCallback<WorkCard> RetryClicked { get; set; }

	/// <summary>Gets or sets the callback raised with the card when one of its tiles asks to close it out.</summary>
	[Parameter]
	public EventCallback<WorkCard> CloseClicked { get; set; }

	/// <summary>Gets or sets why a Retry or Close was refused, by card identifier; the tile of that card shows the message.</summary>
	[Parameter]
	public IReadOnlyDictionary<int, string> Refusals { get; set; } = ReadOnlyDictionary<int, string>.Empty;

	/// <summary>Moves the keyboard focus into the lane: to the name button of a card's tile, or to the lane's heading when the lane has no tile for that card.</summary>
	/// <param name="cardId">The identifier of the card to focus, or <see langword="null"/> for the heading.</param>
	/// <returns>A task that completes when the browser has been asked to.</returns>
	public Task FocusAsync(int? cardId)
	{
		return cardId is int id && _tiles.TryGetValue(id, out CardTile? tile) && tile is not null ? tile.FocusNameAsync() : _heading.FocusAsync().AsTask();
	}

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		foreach (int id in _tiles.Keys.Where(id => Cards.All(card => card.Card.Id != id)).ToList())
		{
			_tiles.Remove(id);
		}
	}

	private string Title => Lane.DisplayName();

	// The one place a lane is given its color: the classes are in this component's stylesheet and read the lane tokens.
	private string DotClass => Lane switch
	{
		CardLane.Queue => "lane-dot lane-dot-queue",
		CardLane.InProgress => "lane-dot lane-dot-in-progress",
		CardLane.Gates => "lane-dot lane-dot-gates",
		CardLane.Review => "lane-dot lane-dot-review",
		CardLane.Complete => "lane-dot lane-dot-complete",
		_ => "lane-dot",
	};

	private string? RefusalOf(BoardCard card)
	{
		return Refusals.GetValueOrDefault(card.Card.Id);
	}
}
