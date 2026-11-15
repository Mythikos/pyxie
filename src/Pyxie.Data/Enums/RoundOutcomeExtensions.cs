namespace Pyxie.Data.Enums;

/// <summary>Presentation and classification helpers for <see cref="RoundOutcome"/>.</summary>
public static class RoundOutcomeExtensions
{
	/// <summary>Gets the name people see for a round outcome in card history.</summary>
	/// <param name="outcome">The outcome, or <see langword="null"/> while the round is running.</param>
	/// <returns>The display name.</returns>
	public static string DisplayName(this RoundOutcome? outcome)
	{
		return outcome switch
		{
			null => "Running",
			RoundOutcome.Completed => "Completed",
			RoundOutcome.Passed => "Passed",
			RoundOutcome.Failed => "Failed",
			RoundOutcome.Approved => "Approved",
			RoundOutcome.Rejected => "Rejected",
			RoundOutcome.Merged => "Merged",
			RoundOutcome.Conflicted => "Conflicted",
			RoundOutcome.Outdated => "Outdated",
			RoundOutcome.Resolved => "Resolved",
			RoundOutcome.Skipped => "Skipped",
			RoundOutcome.Errored => "Errored",
			RoundOutcome.Blocked => "Blocked",
			RoundOutcome.Interrupted => "Interrupted",
			_ => outcome.Value.ToString(),
		};
	}

	/// <summary>Determines whether an outcome sent the card back for more work: a failed gate, a rejection, or a merge conflict.</summary>
	/// <param name="outcome">The outcome.</param>
	/// <returns><see langword="true"/> for <see cref="RoundOutcome.Failed"/>, <see cref="RoundOutcome.Rejected"/>, and <see cref="RoundOutcome.Conflicted"/>.</returns>
	public static bool IsSendBack(this RoundOutcome outcome)
	{
		return outcome is RoundOutcome.Failed or RoundOutcome.Rejected or RoundOutcome.Conflicted;
	}

	/// <summary>Determines whether an outcome uses up one of the card's attempts: every send-back and every recoverable error.</summary>
	/// <param name="outcome">The outcome.</param>
	/// <returns><see langword="true"/> for send-backs and <see cref="RoundOutcome.Errored"/>.</returns>
	public static bool IsSetback(this RoundOutcome outcome)
	{
		return outcome.IsSendBack() || outcome == RoundOutcome.Errored;
	}
}
