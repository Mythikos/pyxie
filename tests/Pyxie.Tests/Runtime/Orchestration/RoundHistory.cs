using Pyxie.Data.Entities;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Writes a card's history the short way, so a test can assert the whole path a card took in one line.</summary>
public static class RoundHistory
{
	/// <summary>Describes rounds as <c>Kind:Outcome</c> pairs separated by commas, in the order given, such as <c>Work:Completed,Gate:Passed,Merge:Merged</c>. A round still running shows as <c>Kind:</c>.</summary>
	/// <param name="rounds">The rounds, oldest first.</param>
	/// <returns>The description.</returns>
	public static string Describe(IEnumerable<CardRound> rounds)
	{
		return string.Join(',', rounds.Select(round => $"{round.Kind}:{round.Outcome}"));
	}
}
