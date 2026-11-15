using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Services.Rules;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>
/// Default <see cref="ICardPipelineService"/> backed by <see cref="ProjectDbContext"/>.
/// Every state change follows one pattern: read the card's <see cref="WorkCard.Version"/> and check the preconditions, then, as the first statement of a transaction, advance the version conditionally on the value read (which also takes the row's write lock), then load the card and its rounds, apply <see cref="PipelineRules"/>, and commit. A writer that loses the race matches no row and re-reads; nothing is ever written from a stale read.
/// </summary>
public sealed class CardPipelineService : ICardPipelineService
{
	// Conflicts come from other workers and from people changing cards and agents; a few re-reads settle them.
	private const int MaxWriteAttempts = 5;

	// A claim passes over cards another worker took meanwhile and cards that stop at once, such as one without a worker, up to this many in one call.
	private const int MaxClaimCandidates = 25;

	private const string CardNotFound = "Card not found.";
	private const string CardBusy = "The card kept changing while the change was being saved. Try again.";
	private const string NotOpen = "This round is no longer open.";
	private const string RecoveredNote = "Pyxie stopped while this round was running.";
	private const string NoWorkerNote = "No worker is assigned.";
	private const string NoRepositoryNote = "No repository is connected.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="CardPipelineService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	/// <param name="timeProvider">The clock that stamps card updates and the start and end of rounds.</param>
	public CardPipelineService(IDbContextFactory<ProjectDbContext> contextFactory, TimeProvider timeProvider)
	{
		_contextFactory = contextFactory;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

		List<int> cardIds = await context.Rounds.Where(round => round.Outcome == null).Select(round => round.CardId).Distinct().ToListAsync(cancellationToken);
		if (cardIds.Count == 0)
		{
			return 0;
		}

		await context.Cards.Where(card => cardIds.Contains(card.Id)).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.Version, card => card.Version + 1).SetProperty(card => card.UpdatedUtc, now), cancellationToken);

		int interrupted = await context.Rounds.Where(round => round.Outcome == null).ExecuteUpdateAsync(changes => changes.SetProperty(round => round.Outcome, (RoundOutcome?)RoundOutcome.Interrupted).SetProperty(round => round.EndedUtc, (DateTime?)now).SetProperty(round => round.Note, RecoveredNote), cancellationToken);

		await transaction.CommitAsync(cancellationToken);

		return interrupted;
	}

	/// <inheritdoc/>
	public async Task<PipelineWork?> ClaimNextAsync(CancellationToken cancellationToken)
	{
		for (int candidate = 0; candidate < MaxClaimCandidates; candidate++)
		{
			await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

			string? targetBranch = await TargetBranchAsync(context, cancellationToken);
			if (targetBranch is null)
			{
				return null;
			}

			(int Id, int Version, CardLane Lane)? found = await FindResumableAsync(context, cancellationToken) ?? await FindQueueHeadAsync(context, cancellationToken);
			if (found is not (int id, int version, CardLane lane))
			{
				return null;
			}

			await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

			if (!await TryAdvanceVersionAsync(context, id, version, now, cancellationToken))
			{
				continue;
			}

			WorkCard card = await LoadForPipelineAsync(context, id, cancellationToken);
			PipelineStep step;
			if (PipelineRules.IsActive(lane))
			{
				// An interrupted card resumes the stage and attempt it was on; no attempt is used.
				CardRound interrupted = card.Rounds.OrderBy(round => round.Sequence).Last();
				step = PipelineStep.Open(interrupted.Kind, card.Attempt);
			}
			else
			{
				// A Queue card starts a new run: attempt 1, at the stage it stopped at or at work.
				step = PipelineStep.Open(card.ResumeRound ?? RoundKind.Work, 1);
				card.ResumeRound = null;
			}

			PipelineSetup setup = await SetupAsync(context, cancellationToken);
			CardRound? round = Advance(card, step, setup, targetBranch, now);
			await PlaceAsync(context, card, lane, cancellationToken);
			await context.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);

			if (round is not null)
			{
				return ToWork(card, round);
			}

			// The card stopped at once and waits in Error; take the next one.
		}

		return null;
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<PipelineWork?>> RecordResultAsync(int roundId, RoundResult result, RoundReport report, CancellationToken cancellationToken)
	{
		string? note = NormalizeNote(report.Note);
		string? reportText = string.IsNullOrWhiteSpace(report.Report) ? null : report.Report;
		string? inputError = ValidateNote(note) ?? (reportText is null ? null : InputRules.ValidateMaxLength(reportText, "Report", PyxieLimits.MaxRoundReportLength));
		if (inputError is not null)
		{
			return ServiceResult<PipelineWork?>.Failure(inputError);
		}

		for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
		{
			await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

			var round = await context.Rounds
				.AsNoTracking()
				.Where(existing => existing.Id == roundId)
				.Select(existing => new { existing.CardId, existing.Kind })
				.FirstOrDefaultAsync(cancellationToken);

			if (round is null)
			{
				return ServiceResult<PipelineWork?>.Failure("Round not found.");
			}

			if (PipelineRules.OutcomeOf(round.Kind, result) is not RoundOutcome outcome)
			{
				return ServiceResult<PipelineWork?>.Failure(InvalidResultMessage(round.Kind, result));
			}

			if (PipelineRules.RequiresNote(outcome) && note is null)
			{
				return ServiceResult<PipelineWork?>.Failure("A note is required when a round fails, is rejected, conflicts, errors, or is blocked.");
			}

			// The version is read before whether the round is open, so a compare-and-set that matches the version proves the round was still open when it was read.
			if (await ReadVersionAsync(context, round.CardId, cancellationToken) is not int version)
			{
				return ServiceResult<PipelineWork?>.Failure(CardNotFound);
			}

			if (!await IsOpenAsync(context, roundId, cancellationToken))
			{
				return ServiceResult<PipelineWork?>.Failure(NotOpen);
			}

			string? targetBranch = await TargetBranchAsync(context, cancellationToken);

			await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

			// The version only changes together with the card's rounds, so a matching version proves the round is still open and still this caller's.
			if (!await TryAdvanceVersionAsync(context, round.CardId, version, now, cancellationToken))
			{
				continue;
			}

			WorkCard card = await LoadForPipelineAsync(context, round.CardId, cancellationToken);
			CardLane sourceLane = card.Lane;
			CardRound finished = card.Rounds.Single(existing => existing.Id == roundId);
			if (finished.Outcome is not null)
			{
				return ServiceResult<PipelineWork?>.Failure(NotOpen);
			}

			finished.Outcome = outcome;
			finished.EndedUtc = now;
			finished.Note = note;
			finished.Report = reportText;

			// The next round opens after the finished one is closed, so its briefing includes the result just recorded.
			PipelineSetup setup = await SetupAsync(context, cancellationToken);
			PipelineStep step = PipelineRules.Next(finished.Kind, outcome, Progress(card, setup));
			CardRound? next = Advance(card, step, setup, targetBranch, now);
			await PlaceAsync(context, card, sourceLane, cancellationToken);
			await context.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);

			return ServiceResult<PipelineWork?>.Success(next is null ? null : ToWork(card, next));
		}

		return ServiceResult<PipelineWork?>.Failure(CardBusy);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> InterruptAsync(int roundId, string note, CancellationToken cancellationToken)
	{
		string? normalizedNote = NormalizeNote(note);
		string? inputError = normalizedNote is null ? "A note is required when a round is interrupted." : ValidateNote(normalizedNote);
		if (inputError is not null)
		{
			return ServiceResult.Failure(inputError);
		}

		for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
		{
			await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

			int? cardId = await context.Rounds
				.AsNoTracking()
				.Where(existing => existing.Id == roundId)
				.Select(existing => (int?)existing.CardId)
				.FirstOrDefaultAsync(cancellationToken);

			if (cardId is not int roundCardId)
			{
				return ServiceResult.Failure("Round not found.");
			}

			if (await ReadVersionAsync(context, roundCardId, cancellationToken) is not int version)
			{
				return ServiceResult.Failure(CardNotFound);
			}

			if (!await IsOpenAsync(context, roundId, cancellationToken))
			{
				return ServiceResult.Failure(NotOpen);
			}

			await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

			if (!await TryAdvanceVersionAsync(context, roundCardId, version, now, cancellationToken))
			{
				continue;
			}

			CardRound running = await context.Rounds.FirstAsync(existing => existing.Id == roundId, cancellationToken);
			if (running.Outcome is not null)
			{
				return ServiceResult.Failure(NotOpen);
			}

			running.Outcome = RoundOutcome.Interrupted;
			running.EndedUtc = now;
			running.Note = normalizedNote;
			await context.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);

			return ServiceResult.Success();
		}

		return ServiceResult.Failure(CardBusy);
	}

	private static string InvalidResultMessage(RoundKind kind, RoundResult result)
	{
		return (kind, result) switch
		{
			(RoundKind.Work, RoundResult.Failed) => "A work round cannot fail. Report Errored or Blocked with the reason instead.",
			(RoundKind.Resolve, RoundResult.Failed) => "A resolve round cannot fail. Report Errored or Blocked with the reason instead.",
			(_, RoundResult.Outdated) => "Only a merge round can be outdated.",
			_ => "Result not recognized.",
		};
	}

	private static string? NormalizeNote(string? note)
	{
		return string.IsNullOrWhiteSpace(note) ? null : note.Trim();
	}

	private static string? ValidateNote(string? note)
	{
		return note is null ? null : InputRules.ValidateMaxLength(note, "Note", PyxieLimits.MaxRoundNoteLength);
	}

	private static CardProgress Progress(WorkCard card, PipelineSetup setup)
	{
		return new CardProgress(card.Attempt, card.AttemptLimit, setup.HasGates, card.ReviewerAgent is not null, setup.Merger is not null, PipelineRules.IsApprovedSinceLastWork(card.Rounds));
	}

	/// <summary>Carries out a step and every step that follows at once, until a round opens or the card's run ends. Stages that do not apply are recorded as skipped, a round that cannot open is recorded as blocked, and the card's lane, state, attempt, and resume stage are set to where it ends up. Placing it in that lane is left to the caller.</summary>
	/// <returns>The round that opened, or <see langword="null"/> when the card completed or stopped.</returns>
	private static CardRound? Advance(WorkCard card, PipelineStep step, PipelineSetup setup, string? targetBranch, DateTime now)
	{
		while (true)
		{
			card.Attempt = step.Attempt;

			if (step.Kind == PipelineStepKind.Complete)
			{
				card.Lane = CardLane.Complete;
				return null;
			}

			if (step.Kind == PipelineStepKind.Stop)
			{
				card.Lane = CardLifecycleRules.EntryLane;
				card.State = CardState.Error;
				card.ResumeRound = step.Round == RoundKind.Work ? null : step.Round;
				return null;
			}

			CardProgress progress = Progress(card, setup);
			RoundKind kind = PipelineRules.KindToOpen(step.Round ?? throw new InvalidOperationException("An open step names its round."), progress);

			if (targetBranch is null)
			{
				AddFinishedRound(card, kind, RoundOutcome.Blocked, NoRepositoryNote, now);
				step = PipelineRules.Next(kind, RoundOutcome.Blocked, progress);
				continue;
			}

			if (kind == RoundKind.Work && card.WorkerAgent is null)
			{
				AddFinishedRound(card, kind, RoundOutcome.Blocked, NoWorkerNote, now);
				step = PipelineRules.Next(kind, RoundOutcome.Blocked, progress);
				continue;
			}

			if (PipelineRules.SkipReason(kind, progress) is string skipped)
			{
				AddFinishedRound(card, kind, RoundOutcome.Skipped, skipped, now);
				step = PipelineRules.Next(kind, RoundOutcome.Skipped, Progress(card, setup));
				continue;
			}

			card.Lane = PipelineRules.LaneOf(kind);

			return OpenRound(card, kind, setup.Merger, targetBranch, now);
		}
	}

	// Copies the agent for the role (the card's worker or reviewer, or the project's merge agent) and builds the briefing from the card's finished rounds; gate and merge rounds run commands and have neither.
	private static CardRound OpenRound(WorkCard card, RoundKind kind, Agent? merger, string targetBranch, DateTime now)
	{
		int sequence = NextSequence(card);
		Agent? agent = kind switch
		{
			RoundKind.Work => card.WorkerAgent,
			RoundKind.Review => card.ReviewerAgent,
			RoundKind.Resolve => merger,
			_ => null,
		};

		CardRound round = new()
		{
			Sequence = sequence,
			Kind = kind,
			Attempt = card.Attempt,
			Agent = agent is null ? null : AgentSnapshot.From(agent),
			Briefing = kind.RunsAgent() ? CardBriefingBuilder.Build(card, kind, sequence, [.. card.Rounds], targetBranch) : null,
			StartedUtc = now,
		};

		card.Rounds.Add(round);

		return round;
	}

	// Records a stage that never ran: skipped because it does not apply, or blocked because it cannot open.
	private static void AddFinishedRound(WorkCard card, RoundKind kind, RoundOutcome outcome, string note, DateTime now)
	{
		card.Rounds.Add(new CardRound
		{
			Sequence = NextSequence(card),
			Kind = kind,
			Attempt = card.Attempt,
			StartedUtc = now,
			EndedUtc = now,
			Outcome = outcome,
			Note = note,
		});
	}

	private static int NextSequence(WorkCard card)
	{
		return card.Rounds.Count == 0 ? 1 : card.Rounds.Max(round => round.Sequence) + 1;
	}

	private static PipelineWork ToWork(WorkCard card, CardRound round)
	{
		return new PipelineWork(card.Id, card.Name, round.Id, round.Sequence, round.Kind, round.Attempt, round.Agent, round.Briefing);
	}

	private static async Task<string?> TargetBranchAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		return await context.Repositories.AsNoTracking().Select(repository => repository.TargetBranch).FirstOrDefaultAsync(cancellationToken);
	}

	private static async Task<PipelineSetup> SetupAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		bool hasGates = await context.Gates.AnyAsync(gate => gate.IsEnabled, cancellationToken);

		// The agent service keeps at most one merge agent; the order only makes the choice stable should that ever not hold.
		Agent? merger = await context.Agents.AsNoTracking().Where(agent => agent.CanMerge).OrderBy(agent => agent.Id).FirstOrDefaultAsync(cancellationToken);

		return new PipelineSetup(hasGates, merger);
	}

	// Later stages first, so cards closest to done finish before new work starts.
	private static async Task<(int Id, int Version, CardLane Lane)?> FindResumableAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		CardLane[] activeLanes = [.. PipelineRules.ActiveLanes];

		var card = await context.Cards
			.AsNoTracking()
			.Where(existing => activeLanes.Contains(existing.Lane)
				&& !existing.Rounds.Any(round => round.Outcome == null)
				&& existing.Rounds.OrderByDescending(round => round.Sequence).Select(round => round.Outcome).FirstOrDefault() == RoundOutcome.Interrupted)
			.OrderByDescending(existing => existing.Lane)
			.ThenBy(existing => existing.SortOrder)
			.ThenBy(existing => existing.Id)
			.Select(existing => new { existing.Id, existing.Version, existing.Lane })
			.FirstOrDefaultAsync(cancellationToken);

		return card is null ? null : (card.Id, card.Version, card.Lane);
	}

	// The top Ready card; cards in Error wait for a person and never hold up the cards behind them.
	private static async Task<(int Id, int Version, CardLane Lane)?> FindQueueHeadAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		CardLane entryLane = CardLifecycleRules.EntryLane;

		var card = await context.Cards
			.AsNoTracking()
			.Where(existing => existing.Lane == entryLane && existing.State == CardState.Ready)
			.OrderBy(existing => existing.SortOrder)
			.ThenBy(existing => existing.Id)
			.Select(existing => new { existing.Id, existing.Version, existing.Lane })
			.FirstOrDefaultAsync(cancellationToken);

		return card is null ? null : (card.Id, card.Version, card.Lane);
	}

	private static async Task<bool> IsOpenAsync(ProjectDbContext context, int roundId, CancellationToken cancellationToken)
	{
		return await context.Rounds.AsNoTracking().AnyAsync(round => round.Id == roundId && round.Outcome == null, cancellationToken);
	}

	private static async Task<int?> ReadVersionAsync(ProjectDbContext context, int cardId, CancellationToken cancellationToken)
	{
		return await context.Cards
			.AsNoTracking()
			.Where(card => card.Id == cardId)
			.Select(card => (int?)card.Version)
			.FirstOrDefaultAsync(cancellationToken);
	}

	// The transaction's first statement: it only matches while the card is unchanged since it was read, and takes the row's write lock until commit.
	private static async Task<bool> TryAdvanceVersionAsync(ProjectDbContext context, int cardId, int expectedVersion, DateTime now, CancellationToken cancellationToken)
	{
		int updated = await context.Cards.Where(card => card.Id == cardId && card.Version == expectedVersion).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.Version, card => card.Version + 1).SetProperty(card => card.UpdatedUtc, now), cancellationToken);

		return updated == 1;
	}

	private static async Task<WorkCard> LoadForPipelineAsync(ProjectDbContext context, int cardId, CancellationToken cancellationToken)
	{
		return await context.Cards.Include(card => card.Category).Include(card => card.WorkerAgent).Include(card => card.ReviewerAgent).Include(card => card.Rounds).FirstAsync(card => card.Id == cardId, cancellationToken);
	}

	// A stopped card goes to the top of the Queue, where it runs next once retried; any other card that changed lanes goes to the bottom of its new lane. Either way the lane it left closes the gap.
	private static async Task PlaceAsync(ProjectDbContext context, WorkCard card, CardLane sourceLane, CancellationToken cancellationToken)
	{
		bool stopped = card.Lane == CardLifecycleRules.EntryLane && card.State == CardState.Error;
		if (!stopped && card.Lane == sourceLane)
		{
			return;
		}

		List<WorkCard> others = await context.Cards.Where(existing => existing.Lane == card.Lane && existing.Id != card.Id).OrderBy(existing => existing.SortOrder).ThenBy(existing => existing.Id).ToListAsync(cancellationToken);

		if (stopped)
		{
			card.SortOrder = 0;
			LaneSequencer.Resequence(others, firstPosition: 1);
		}
		else
		{
			card.SortOrder = others.Count == 0 ? 0 : others.Max(existing => existing.SortOrder) + 1;
		}

		if (sourceLane != card.Lane)
		{
			List<WorkCard> source = await context.Cards.Where(existing => existing.Lane == sourceLane && existing.Id != card.Id).OrderBy(existing => existing.SortOrder).ThenBy(existing => existing.Id).ToListAsync(cancellationToken);

			LaneSequencer.Resequence(source);
		}
	}
}
