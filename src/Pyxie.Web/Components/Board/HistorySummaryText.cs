using Pyxie.Data.Services.ReadModels;

namespace Pyxie.Web.Components.Board;

/// <summary>Words the summary line above a card's history.</summary>
public static class HistorySummaryText
{
	/// <summary>Gets the summary line of a card's history.</summary>
	/// <param name="summary">What the card's rounds add up to.</param>
	/// <returns>The round counts and the send-backs, followed by <c>running</c> while a round is in progress. Resolve rounds are counted only on a card that had one, since most cards never conflict.</returns>
	public static string Of(CardHistorySummary summary)
	{
		string resolves = summary.ResolveRounds > 0 ? $", {summary.ResolveRounds} resolve" : string.Empty;
		string counts = $"{summary.WorkRounds} work, {summary.GateRounds} gate, {summary.ReviewRounds} review{resolves}, {summary.SendBacks} sent back";

		return summary.IsRunning ? $"{counts}, running" : counts;
	}
}
