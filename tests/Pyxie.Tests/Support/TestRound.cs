using Pyxie.Data.Entities;
using Pyxie.Data.Enums;

namespace Pyxie.Tests.Support;

/// <summary>Builds in-memory rounds for tests of code that reads a card's history without a database.</summary>
public static class TestRound
{
	/// <summary>Creates a round as the pipeline would have recorded it.</summary>
	/// <param name="sequence">The round's sequence in the card's history.</param>
	/// <param name="kind">The round kind.</param>
	/// <param name="outcome">How the round ended, or <see langword="null"/> while it runs.</param>
	/// <param name="agentName">The recorded agent name, or <see langword="null"/> for a round without an agent.</param>
	/// <param name="note">The round note.</param>
	/// <param name="report">The round report.</param>
	/// <param name="attempt">The attempt of the card's run the round belongs to; 1 unless given.</param>
	/// <param name="startedUtc">When the round started; the default <see cref="DateTime"/> unless given.</param>
	/// <param name="endedUtc">When the round ended; <see langword="null"/> unless given.</param>
	/// <returns>The round.</returns>
	public static CardRound Create(int sequence, RoundKind kind, RoundOutcome? outcome, string? agentName = null, string? note = null, string? report = null, int attempt = 1, DateTime startedUtc = default, DateTime? endedUtc = null)
	{
		return new CardRound
		{
			Id = sequence,
			Sequence = sequence,
			Kind = kind,
			Attempt = attempt,
			Agent = agentName is null ? null : new AgentSnapshot(agentName, HarnessKind.ClaudeCode, "claude", string.Empty),
			StartedUtc = startedUtc,
			EndedUtc = endedUtc,
			Outcome = outcome,
			Note = note,
			Report = report,
		};
	}
}
