using Pyxie.Data.Entities;

namespace Pyxie.Tests.Support;

/// <summary>Reads a card's history as one short line per round, so a test states the whole sequence of rounds, attempts, and outcomes in a single assertion.</summary>
public static class PipelineRoundLog
{
	/// <summary>The word for a round that has no outcome yet.</summary>
	public const string Open = "Open";

	/// <summary>Describes a round as its kind, attempt, and outcome, for example <c>Gate@2:Failed</c>, or <c>Work@3:Open</c> while the round runs.</summary>
	/// <param name="round">The round.</param>
	/// <returns>The line.</returns>
	public static string Describe(CardRound round)
	{
		return $"{round.Kind}@{round.Attempt}:{round.Outcome?.ToString() ?? Open}";
	}

	/// <summary>Reads the lines of a card's rounds, oldest first.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>One line per round in sequence order.</returns>
	public static async Task<List<string>> ReadAsync(TestDatabase database, int cardId)
	{
		List<CardRound> rounds = await TestData.ReadRoundsAsync(database, cardId);

		return [.. rounds.Select(Describe)];
	}
}
