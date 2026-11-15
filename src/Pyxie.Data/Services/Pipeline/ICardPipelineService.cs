using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>The operations the pipeline (the orchestrator that runs harnesses, gates, and merges) uses to drive cards. It never moves cards by lane: it claims work, runs it, and reports the result of the round it owns; <see cref="Rules.PipelineRules"/> decides where the card goes and records the history. Every operation is safe for several pipeline workers running at once and for people editing, retrying, or closing cards meanwhile: writes are conditional on <see cref="Entities.WorkCard.Version"/> and on the round still being open. See <c>docs/pipeline.md</c>.</summary>
public interface ICardPipelineService
{
	/// <summary>Marks every running round as <see cref="RoundOutcome.Interrupted"/>. Call it once when the pipeline starts, before any worker runs: a round still open at that point belonged to a process that stopped. Interrupted cards are resumed at the same stage by <see cref="ClaimNextAsync"/>.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The number of rounds marked interrupted.</returns>
	Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken);

	/// <summary>Claims the next card and opens a round for it. Interrupted cards come first, resumed at the stage and attempt they were on (later stages first; a resolve round opens as a work round when the project no longer has a merge agent); then the top Ready card of the Queue, which starts attempt 1 of a new run at <see cref="Entities.WorkCard.ResumeRound"/> or at work. Cards in Error are never claimed. Stages that do not apply are recorded as skipped on the way, and a card that is stopped at once (for example it has no worker) is passed over for the next one.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The opened round, or <see langword="null"/> when no repository is connected, there is nothing to claim, or every candidate was taken by another worker meanwhile.</returns>
	Task<PipelineWork?> ClaimNextAsync(CancellationToken cancellationToken);

	/// <summary>Records the result of an open round and applies what follows (<see cref="Rules.PipelineRules.Next"/>): the round closes with its outcome, and the card either moves on and opens its next round, completes, or stops in Error at the top of the Queue.</summary>
	/// <param name="roundId">The round the caller owns, from <see cref="PipelineWork.RoundId"/>.</param>
	/// <param name="result">What happened.</param>
	/// <param name="report">The note and report to record. A note is required for failed, errored, and blocked rounds.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The next round to run, or a successful <see langword="null"/> when the card's run has ended (complete or stopped). A failure when the round does not exist or is no longer open (for example it was interrupted), the result is not valid for the round kind (a work or resolve round cannot fail; only a merge can be outdated), the note is missing where one is required, or the note or report is too long.</returns>
	Task<ServiceResult<PipelineWork?>> RecordResultAsync(int roundId, RoundResult result, RoundReport report, CancellationToken cancellationToken);

	/// <summary>
	/// Gives back a round the caller owns without a result, because the pipeline is being stopped or Pyxie is shutting down. The round closes as
	/// <see cref="RoundOutcome.Interrupted"/>, the card stays in its lane, and <see cref="ClaimNextAsync"/> resumes it at the same stage and attempt. No attempt is used.
	/// </summary>
	/// <param name="roundId">The round the caller owns.</param>
	/// <param name="note">The reason recorded on the round, at most <see cref="Limits.PyxieLimits.MaxRoundNoteLength"/> characters.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the round does not exist or is no longer open, or the note is blank or too long.</returns>
	Task<ServiceResult> InterruptAsync(int roundId, string note, CancellationToken cancellationToken);
}
