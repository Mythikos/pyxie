using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;

namespace Pyxie.Web.Components.Board;

/// <summary>The cells of one history row: number, kind, attempt, agent, outcome, and start time.</summary>
public partial class RoundLine
{
	/// <summary>Gets or sets the round to show.</summary>
	[Parameter]
	public required CardRound Round { get; set; }

	private string AttemptText => Round.Attempt > 0 ? $"Attempt {Round.Attempt}" : string.Empty;

	private string OutcomeClass => $"round-outcome round-tone-{RoundOutcomeTones.Of(Round.Outcome).ToString().ToLowerInvariant()}";

	private string Time => DateTime.SpecifyKind(Round.StartedUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
