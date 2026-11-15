using Microsoft.AspNetCore.Components;

using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;

namespace Pyxie.Web.Components.Board;

/// <summary>A card's rounds as a row of small squares, oldest first: green for what went well, red for a setback, gray for what was skipped or interrupted, amber for work made stale, and an outline in the stage's color for the round running now. The rounds are also written out as the row's text alternative.</summary>
public partial class RoundTrail : ComponentBase
{
	/// <summary>The most rounds the trail shows. Older rounds are left out of the trail only; the card's history keeps them.</summary>
	public const int MaxShown = 20;

	/// <summary>Gets or sets the card's rounds, oldest first.</summary>
	[Parameter]
	[EditorRequired]
	public required IReadOnlyList<RoundSummary> Rounds { get; set; }

	private IEnumerable<RoundSummary> Shown => Rounds.Skip(Math.Max(0, Rounds.Count - MaxShown));

	// Written out as the design words it: "Rounds: work completed, gate failed, work running". The kind is the enum name, not the display name, which calls the gate stage "Gates".
	private string Label => "Rounds: " + string.Join(", ", Shown.Select(round => $"{round.Kind.ToString().ToLowerInvariant()} {round.Outcome.DisplayName().ToLowerInvariant()}"));

	private static string SquareClass(RoundSummary round)
	{
		return round.Outcome is null
			? $"trail-square trail-open trail-stage-{RoundStageTones.Of(round.Kind).ToString().ToLowerInvariant()}"
			: $"trail-square trail-tone-{RoundOutcomeTones.Of(round.Outcome).ToString().ToLowerInvariant()}";
	}
}
