using System.Globalization;

using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.ReadModels;

/// <summary>What a card's rounds add up to, for the board.</summary>
/// <param name="WorkRounds">The number of work rounds.</param>
/// <param name="GateRounds">The number of gate rounds, not counting skipped ones.</param>
/// <param name="ReviewRounds">The number of review rounds, not counting skipped ones.</param>
/// <param name="ResolveRounds">The number of resolve rounds, in which the merge agent took on a merge conflict.</param>
/// <param name="SendBacks">The number of times the card was sent back for more work: gate failures, rejections, and merge conflicts.</param>
/// <param name="IsRunning">Whether a round is running now.</param>
/// <param name="StopReason">Why the pipeline stopped the card, while it is in Error; otherwise <see langword="null"/>.</param>
public sealed record CardHistorySummary(
	int WorkRounds,
	int GateRounds,
	int ReviewRounds,
	int ResolveRounds,
	int SendBacks,
	bool IsRunning,
	string? StopReason)
{
	private const string StoppedNote = "Stopped by the pipeline.";

	/// <summary>Gets the summary of a card with no rounds.</summary>
	public static CardHistorySummary Empty { get; } = new(0, 0, 0, 0, 0, false, null);

	/// <summary>Summarizes a card's rounds.</summary>
	/// <param name="state">The card's state.</param>
	/// <param name="rounds">The card's rounds, in any order.</param>
	/// <returns>The summary.</returns>
	public static CardHistorySummary From(CardState state, IEnumerable<RoundSummary> rounds)
	{
		IReadOnlyCollection<RoundSummary> history = rounds as IReadOnlyCollection<RoundSummary> ?? rounds.ToArray();
		var counts = history.GroupBy(round => (round.Kind, round.Outcome)).Select(group => (group.Key.Kind, group.Key.Outcome, group.Count()));
		return FromCounts(state, counts, history.MaxBy(round => round.Sequence));
	}

	internal static CardHistorySummary FromCounts(CardState state, IEnumerable<(RoundKind Kind, RoundOutcome? Outcome, int Count)> counts, RoundSummary? last)
	{
		var groups = counts.ToArray();
		if (groups.Length == 0)
		{
			return state == CardState.Error ? Empty with { StopReason = StoppedNote } : Empty;
		}

		int Held(RoundKind kind) => groups.Where(group => group.Kind == kind && group.Outcome != RoundOutcome.Skipped).Sum(group => group.Count);
		return new CardHistorySummary(
			WorkRounds: Held(RoundKind.Work),
			GateRounds: Held(RoundKind.Gate),
			ReviewRounds: Held(RoundKind.Review),
			ResolveRounds: Held(RoundKind.Resolve),
			SendBacks: groups.Where(group => group.Outcome is RoundOutcome outcome && outcome.IsSendBack()).Sum(group => group.Count),
			IsRunning: groups.Any(group => group.Outcome is null),
			StopReason: state == CardState.Error ? StopReasonOf(last) : null);
	}

	// A blocked round says what it needs; a card that ran out of attempts says how many it used and what the last setback was.
	private static string StopReasonOf(RoundSummary? last)
	{
		if (last is null)
		{
			return StoppedNote;
		}

		if (last.Outcome == RoundOutcome.Blocked)
		{
			return last.Note ?? StoppedNote;
		}

		if (last.Outcome is RoundOutcome outcome && outcome.IsSetback())
		{
			string attempts = last.Attempt == 1 ? "1 attempt" : string.Create(CultureInfo.InvariantCulture, $"{last.Attempt} attempts");
			string setback = $"{last.Kind.DisplayName()} {last.Outcome.DisplayName()}";

			return last.Note is null ? $"Stopped after {attempts}. {setback}." : $"Stopped after {attempts}. {setback}: {last.Note}";
		}

		return StoppedNote;
	}
}
