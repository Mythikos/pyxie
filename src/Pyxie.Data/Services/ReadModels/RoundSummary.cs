using Pyxie.Data.Entities;
using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.ReadModels;

/// <summary>The fields of a round the board needs, without the briefing and report text.</summary>
/// <param name="CardId">The card the round belongs to.</param>
/// <param name="Sequence">The one-based position of the round in the card's history.</param>
/// <param name="Kind">The stage the round ran.</param>
/// <param name="Attempt">The attempt of the card's run the round belongs to.</param>
/// <param name="Outcome">How the round ended, or <see langword="null"/> while it is running.</param>
/// <param name="Note">The short reason recorded with the outcome, if any.</param>
/// <param name="AgentName">The name of the agent that ran it, as recorded; <see langword="null"/> for rounds that ran no agent.</param>
/// <param name="StartedUtc">When the round started.</param>
/// <param name="EndedUtc">When the round ended, or <see langword="null"/> while it is running.</param>
public sealed record RoundSummary(int CardId, int Sequence, RoundKind Kind, int Attempt, RoundOutcome? Outcome, string? Note, string? AgentName, DateTime StartedUtc, DateTime? EndedUtc)
{
	/// <summary>Summarizes a stored round, so a card's history is counted by <see cref="CardHistorySummary.From"/> wherever it was loaded.</summary>
	/// <param name="round">The round.</param>
	/// <returns>The summary.</returns>
	public static RoundSummary From(CardRound round)
	{
		return new RoundSummary(round.CardId, round.Sequence, round.Kind, round.Attempt, round.Outcome, round.Note, round.Agent?.Name, round.StartedUtc, round.EndedUtc);
	}
}
