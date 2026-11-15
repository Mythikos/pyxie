using Pyxie.Data.Enums;

namespace Pyxie.Web.Components.Board;

/// <summary>Chooses the <see cref="RoundOutcomeTone"/> of a round outcome. The round trail on a card and the history in the card dialog both tone through here, so they always agree.</summary>
public static class RoundOutcomeTones
{
	/// <summary>Gets the tone of an outcome.</summary>
	/// <param name="outcome">The outcome, or <see langword="null"/> while the round is running.</param>
	/// <returns>
	/// The tone. Every defined outcome has one other than <see cref="RoundOutcomeTone.None"/>, which is reserved for a round that is still running;
	/// a value outside the enumeration is toned like a running round.
	/// </returns>
	public static RoundOutcomeTone Of(RoundOutcome? outcome)
	{
		return outcome switch
		{
			RoundOutcome.Completed or RoundOutcome.Passed or RoundOutcome.Approved or RoundOutcome.Resolved or RoundOutcome.Merged => RoundOutcomeTone.Success,
			RoundOutcome.Failed or RoundOutcome.Rejected or RoundOutcome.Conflicted or RoundOutcome.Errored or RoundOutcome.Blocked => RoundOutcomeTone.Danger,
			RoundOutcome.Outdated => RoundOutcomeTone.Warning,
			RoundOutcome.Skipped or RoundOutcome.Interrupted => RoundOutcomeTone.Muted,
			_ => RoundOutcomeTone.None,
		};
	}
}
