using Pyxie.Data.Entities;
using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Rules;

/// <summary>Owns the pipeline's state machine. The pipeline never names lanes: it reports a <see cref="RoundResult"/> for the round it is running, <see cref="OutcomeOf"/> turns it into the round's outcome, and <see cref="Next"/> decides what follows from the outcome and the card's progress. Work leads to the gates, passing gates lead to review, approval leads to the merge, and the merge completes the card. A merge conflict goes to the project's merge agent, whose resolution goes through the gates and the review again, or back to the worker when the project has no merge agent. A setback (a send-back or a recoverable error) uses an attempt, and a setback on the last attempt or a blocked round stops the card in Error. See <c>docs/pipeline.md</c>.</summary>
public static class PipelineRules
{
	/// <summary>The note recorded on a gate round that was skipped.</summary>
	public const string NoGatesNote = "No gates are enabled.";

	/// <summary>The note recorded on a review round that was skipped.</summary>
	public const string NoReviewerNote = "No reviewer is assigned.";

	/// <summary>Gets the lanes in which the pipeline owns a card, in pipeline order. People can only review cards in these lanes.</summary>
	public static IReadOnlyList<CardLane> ActiveLanes { get; } = Array.AsReadOnly<CardLane>([CardLane.InProgress, CardLane.Gates, CardLane.Review]);

	/// <summary>Determines whether the pipeline owns cards in a lane.</summary>
	/// <param name="lane">The lane.</param>
	/// <returns><see langword="true"/> for In Progress, Gates, and Review.</returns>
	public static bool IsActive(CardLane lane)
	{
		return lane is CardLane.InProgress or CardLane.Gates or CardLane.Review;
	}

	/// <summary>Gets the lane in which rounds of a kind run. Merge rounds run in Review: the review and the merge together are the card's pull request. Resolve rounds run in In Progress: a resolution is new work on the card.</summary>
	/// <param name="kind">The round kind.</param>
	/// <returns>The lane a card is in while such a round runs.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined round kind.</exception>
	public static CardLane LaneOf(RoundKind kind)
	{
		return kind switch
		{
			RoundKind.Work or RoundKind.Resolve => CardLane.InProgress,
			RoundKind.Gate => CardLane.Gates,
			RoundKind.Review or RoundKind.Merge => CardLane.Review,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown round kind."),
		};
	}

	/// <summary>Decides the outcome of a round from the result the pipeline reported.</summary>
	/// <param name="kind">The kind of the round that finished.</param>
	/// <param name="result">What the pipeline reported.</param>
	/// <returns>The outcome, or <see langword="null"/> when the result is not valid for the round kind (a work or resolve round cannot fail; only a merge can be outdated) or either value is undefined.</returns>
	public static RoundOutcome? OutcomeOf(RoundKind kind, RoundResult result)
	{
		if (!Enum.IsDefined(kind))
		{
			return null;
		}

		return (kind, result) switch
		{
			(RoundKind.Work, RoundResult.Succeeded) => RoundOutcome.Completed,
			(RoundKind.Gate, RoundResult.Succeeded) => RoundOutcome.Passed,
			(RoundKind.Gate, RoundResult.Failed) => RoundOutcome.Failed,
			(RoundKind.Review, RoundResult.Succeeded) => RoundOutcome.Approved,
			(RoundKind.Review, RoundResult.Failed) => RoundOutcome.Rejected,
			(RoundKind.Merge, RoundResult.Succeeded) => RoundOutcome.Merged,
			(RoundKind.Merge, RoundResult.Failed) => RoundOutcome.Conflicted,
			(RoundKind.Merge, RoundResult.Outdated) => RoundOutcome.Outdated,
			(RoundKind.Resolve, RoundResult.Succeeded) => RoundOutcome.Resolved,
			(_, RoundResult.Errored) => RoundOutcome.Errored,
			(_, RoundResult.Blocked) => RoundOutcome.Blocked,
			_ => null,
		};
	}

	/// <summary>Determines whether a round that ends with an outcome must say why, so history and the next briefing can explain it.</summary>
	/// <param name="outcome">The outcome.</param>
	/// <returns><see langword="true"/> for failed, rejected, conflicted, errored, and blocked rounds.</returns>
	public static bool RequiresNote(RoundOutcome outcome)
	{
		return outcome.IsSendBack() || outcome is RoundOutcome.Errored or RoundOutcome.Blocked;
	}

	/// <summary>Decides what follows a finished round.</summary>
	/// <param name="kind">The kind of the round that finished.</param>
	/// <param name="outcome">Its outcome. <see cref="RoundOutcome.Skipped"/> is valid for gate and review rounds.</param>
	/// <param name="progress">The card's progress, with the finished round already part of its history.</param>
	/// <returns>The next step.</returns>
	/// <exception cref="ArgumentException">The outcome does not follow from a round of that kind, or it is <see cref="RoundOutcome.Interrupted"/>, which resumes the same round instead.</exception>
	public static PipelineStep Next(RoundKind kind, RoundOutcome outcome, CardProgress progress)
	{
		int attempt = progress.Attempt;

		return (kind, outcome) switch
		{
			(RoundKind.Work, RoundOutcome.Completed) => PipelineStep.Open(RoundKind.Gate, attempt),
			(RoundKind.Gate, RoundOutcome.Passed or RoundOutcome.Skipped) => PipelineStep.Open(progress.ApprovedSinceLastWork ? RoundKind.Merge : RoundKind.Review, attempt),
			(RoundKind.Review, RoundOutcome.Approved or RoundOutcome.Skipped) => PipelineStep.Open(RoundKind.Merge, attempt),
			(RoundKind.Merge, RoundOutcome.Outdated) => PipelineStep.Open(RoundKind.Gate, attempt),
			(RoundKind.Merge, RoundOutcome.Merged) => PipelineStep.Complete(attempt),
			(RoundKind.Merge, RoundOutcome.Conflicted) => Setback(progress.HasMerger ? RoundKind.Resolve : RoundKind.Work, progress),
			(RoundKind.Resolve, RoundOutcome.Resolved) => PipelineStep.Open(RoundKind.Gate, attempt),
			(RoundKind.Gate, RoundOutcome.Failed) or (RoundKind.Review, RoundOutcome.Rejected) => Setback(RoundKind.Work, progress),
			(_, RoundOutcome.Errored) when Enum.IsDefined(kind) => Setback(kind, progress),
			(_, RoundOutcome.Blocked) when Enum.IsDefined(kind) => PipelineStep.Stop(kind, attempt),
			_ => throw new ArgumentException($"A {kind} round cannot end {outcome}.", nameof(outcome)),
		};
	}

	/// <summary>Gets the kind of round a stage opens as. A resolve stage opens a work round when the project has no merge agent, which happens when the merge agent was removed after the card stopped at, or was interrupted in, a resolve round: the worker then resolves the conflict, as it does in a project that never had a merge agent.</summary>
	/// <param name="kind">The stage about to open.</param>
	/// <param name="progress">The card's progress.</param>
	/// <returns>The kind of round to open.</returns>
	public static RoundKind KindToOpen(RoundKind kind, CardProgress progress)
	{
		return kind == RoundKind.Resolve && !progress.HasMerger ? RoundKind.Work : kind;
	}

	/// <summary>Gives the reason a stage does not apply to a card, so its round is recorded as skipped instead of run.</summary>
	/// <param name="kind">The round about to open.</param>
	/// <param name="progress">The card's progress.</param>
	/// <returns><see cref="NoGatesNote"/> for a gate round without an enabled gate, <see cref="NoReviewerNote"/> for a review round without a reviewer, otherwise <see langword="null"/>.</returns>
	public static string? SkipReason(RoundKind kind, CardProgress progress)
	{
		return kind switch
		{
			RoundKind.Gate when !progress.HasGates => NoGatesNote,
			RoundKind.Review when !progress.HasReviewer => NoReviewerNote,
			_ => null,
		};
	}

	/// <summary>Determines whether the card's latest work was approved, or its review skipped, with no round since that changed the work: a work round, or a resolve round, whose resolution is the merge agent's own work. Pyxie's own merge of a target branch that merged cleanly keeps the approval; the gates check it.</summary>
	/// <param name="rounds">The card's rounds, in any order.</param>
	/// <returns><see langword="true"/> when an approved or skipped review round follows the latest work or resolve round.</returns>
	public static bool IsApprovedSinceLastWork(IEnumerable<CardRound> rounds)
	{
		foreach (CardRound round in rounds.OrderByDescending(round => round.Sequence))
		{
			if (round.Kind.ChangesWork())
			{
				return false;
			}

			if (round is { Kind: RoundKind.Review, Outcome: RoundOutcome.Approved or RoundOutcome.Skipped })
			{
				return true;
			}
		}

		return false;
	}

	// A setback uses the attempt that just ended: the next one opens unless that was the last, and then the card stops, resuming at the same stage after a retry.
	private static PipelineStep Setback(RoundKind next, CardProgress progress)
	{
		return progress.Attempt >= progress.AttemptLimit ? PipelineStep.Stop(next, progress.Attempt) : PipelineStep.Open(next, progress.Attempt + 1);
	}
}
