using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Rules;
using Pyxie.Web.Components.Display;

namespace Pyxie.Web.Components.Board;

/// <summary>A card on the board. Every tile shows the category and the attempts; what follows depends on where the card stands: who will work on a card waiting in the Queue, why the pipeline stopped a card in Error and the way to retry it, the round trail and the running round of a card the pipeline is working on, and when a Complete card finished with the way to close it out.</summary>
public partial class CardTile : ComponentBase
{
	/// <summary>Gets or sets the card to display.</summary>
	[Parameter]
	public required BoardCard Card { get; set; }

	/// <summary>Gets or sets the callback raised with the card when the tile is clicked, or its Edit button is.</summary>
	[Parameter]
	public EventCallback<WorkCard> Clicked { get; set; }

	/// <summary>Gets or sets the callback raised with the card when its Retry button is clicked.</summary>
	[Parameter]
	public EventCallback<WorkCard> RetryClicked { get; set; }

	/// <summary>Gets or sets the callback raised with the card when its Close button is clicked.</summary>
	[Parameter]
	public EventCallback<WorkCard> CloseClicked { get; set; }

	/// <summary>Gets or sets why the last Retry or Close on this card was refused, shown on the card until the board moves on, or <see langword="null"/> when nothing was.</summary>
	[Parameter]
	public string? Refusal { get; set; }

	/// <summary>Gets or sets the clock the elapsed and finish times are read from, supplied by the framework.</summary>
	[Inject]
	public required TimeProvider Time { get; set; }

	// The name button, which every tile has and which stays while the card changes lane or state: where the focus goes when the button that was pressed leaves the tile.
	private ElementReference _name;

	private WorkCard Item => Card.Card;

	private CardHistorySummary History => Card.History;

	private IReadOnlyList<RoundSummary> Rounds => Card.Rounds;

	// The round running now. A card waiting to be claimed, or interrupted because the pipeline stopped, has none; at most one round is open at a time.
	private RoundSummary? OpenRound => Rounds.FirstOrDefault(round => round.Outcome is null);

	private bool IsError => Item.State == CardState.Error;

	private bool IsQueue => Item.Lane == CardLane.Queue;

	private bool IsActive => PipelineRules.IsActive(Item.Lane);

	private bool IsComplete => Item.Lane == CardLane.Complete;

	private bool CanRetry => CardLifecycleRules.CanRetry(Item.Lane, Item.State);

	private bool CanClose => CardLifecycleRules.CanClose(Item.Lane);

	// A card the pipeline stopped is tinted red; one in a lane the pipeline owns is looser; one with a round running casts the design's single shadow.
	private string TileClass
	{
		get
		{
			List<string> classes = ["card-tile"];

			if (IsError)
			{
				classes.Add("card-tile-error");
			}

			if (IsActive)
			{
				classes.Add("card-tile-active");
			}

			if (OpenRound is not null)
			{
				classes.Add("card-tile-running");
			}

			return string.Join(' ', classes);
		}
	}

	private string CountText => AttemptProgress.Compact(Item.Attempt, Item.AttemptLimit);

	// A card that has not run shows only its limit, quietly; a card the pipeline stopped shows how far it got in red.
	private string CountClass => IsError ? "card-count card-count-error" : Item.Attempt == 0 ? "card-count card-count-idle" : "card-count";

	// When the card was merged: the end of its last round. A card without one has nothing to say.
	private string? FinishedText => IsComplete && Rounds.LastOrDefault()?.EndedUtc is DateTime ended ? RelativeTime.Describe(ended, Time.GetUtcNow().UtcDateTime, Time.LocalTimeZone) : null;

	private string ElapsedText => OpenRound is null ? string.Empty : ElapsedTime.Describe(Time.GetUtcNow().UtcDateTime - DateTime.SpecifyKind(OpenRound.StartedUtc, DateTimeKind.Utc));

	/// <summary>Moves the keyboard focus to the card's name button.</summary>
	/// <returns>A task that completes when the browser has been asked to.</returns>
	public async Task FocusNameAsync()
	{
		await _name.FocusAsync();
	}

	private Task OpenAsync()
	{
		return Clicked.InvokeAsync(Item);
	}

	private Task RetryAsync()
	{
		return RetryClicked.InvokeAsync(Item);
	}

	private Task CloseAsync()
	{
		return CloseClicked.InvokeAsync(Item);
	}
}
