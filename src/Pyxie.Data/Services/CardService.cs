using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Services.Rules;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="ICardService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class CardService : ICardService
{
	private const string CardNotFound = "Card not found.";
	private const string CardChanged = "The card changed while it was being saved. Try again.";
	private const string NotStoppedMessage = "Only a card the pipeline stopped can be retried.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="CardService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	/// <param name="timeProvider">The clock that stamps created, updated, and closed times.</param>
	public CardService(IDbContextFactory<ProjectDbContext> contextFactory, TimeProvider timeProvider)
	{
		_contextFactory = contextFactory;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<BoardCard>> GetBoardAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		CardLane[] boardLanes = [.. CardLifecycleRules.BoardLanes];

		List<WorkCard> cards = await QueryWithReferences(context).Where(card => boardLanes.Contains(card.Lane)).OrderBy(card => card.Lane).ThenBy(card => card.SortOrder).ThenBy(card => card.Id).ToListAsync(cancellationToken);

		// Only the fields the summary needs: briefings and reports can be large and the board never shows them.
		int[] cardIds = [.. cards.Select(card => card.Id)];
		List<RoundSummary> rounds = await context.Rounds.AsNoTracking().Where(round => cardIds.Contains(round.CardId)).Select(round => new RoundSummary(round.CardId, round.Sequence, round.Kind, round.Attempt, round.Outcome, round.Note, round.Agent == null ? null : round.Agent.Name, round.StartedUtc, round.EndedUtc)).ToListAsync(cancellationToken);

		ILookup<int, RoundSummary> roundsByCard = rounds.ToLookup(round => round.CardId);

		return [.. cards.Select(card =>
		{
			List<RoundSummary> trail = [.. roundsByCard[card.Id].OrderBy(round => round.Sequence)];

			return new BoardCard(card, CardHistorySummary.From(card.State, trail), trail);
		})];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<BoardCardSummary>> GetBoardPageAsync(int afterId, int count, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		CardLane[] boardLanes = [.. CardLifecycleRules.BoardLanes];
		List<WorkCard> cards = await QueryWithReferences(context)
			.Where(card => card.Id > afterId && boardLanes.Contains(card.Lane))
			.OrderBy(card => card.Id)
			.Take(count)
			.ToListAsync(cancellationToken);
		int[] ids = [.. cards.Select(card => card.Id)];
		var counts = await context.Rounds.Where(round => ids.Contains(round.CardId))
			.GroupBy(round => new { round.CardId, round.Kind, round.Outcome })
			.Select(group => new { group.Key.CardId, group.Key.Kind, group.Key.Outcome, Count = group.Count() })
			.ToListAsync(cancellationToken);
		var countsByCard = counts.ToLookup(row => row.CardId, row => (row.Kind, row.Outcome, row.Count));

		int[] stopped = [.. cards.Where(card => card.State == CardState.Error).Select(card => card.Id)];
		Dictionary<int, RoundSummary> lastRounds = await context.Rounds.AsNoTracking()
			.Where(round => stopped.Contains(round.CardId) && !context.Rounds.Any(later => later.CardId == round.CardId && later.Sequence > round.Sequence))
			.Select(round => new RoundSummary(round.CardId, round.Sequence, round.Kind, round.Attempt, round.Outcome, round.Note, null, round.StartedUtc, round.EndedUtc))
			.ToDictionaryAsync(round => round.CardId, cancellationToken);
		return [.. cards.Select(card => new BoardCardSummary(card, CardHistorySummary.FromCounts(card.State, countsByCard[card.Id], lastRounds.GetValueOrDefault(card.Id))))];
	}

	/// <inheritdoc/>
	public Task<ServiceResult<IReadOnlyList<CardRound>>> GetHistoryAsync(int id, CancellationToken cancellationToken) => GetHistoryPageAsync(id, 1, int.MaxValue, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult<IReadOnlyList<CardRound>>> GetHistoryPageAsync(int id, int firstSequence, int count, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (!await context.Cards.AsNoTracking().AnyAsync(card => card.Id == id, cancellationToken))
		{
			return ServiceResult<IReadOnlyList<CardRound>>.Failure(CardNotFound);
		}

		List<CardRound> rounds = await context.Rounds
			.AsNoTracking()
			.Where(round => round.CardId == id && round.Sequence >= firstSequence)
			.OrderBy(round => round.Sequence)
			.Take(count)
			.ToListAsync(cancellationToken);

		return ServiceResult<IReadOnlyList<CardRound>>.Success(rounds);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<WorkCard>> GetAsync(int id, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		WorkCard? card = await QueryWithReferences(context).FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

		return card is null ? ServiceResult<WorkCard>.Failure(CardNotFound) : ServiceResult<WorkCard>.Success(card);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<WorkCard>> CreateAsync(CardInput input, CancellationToken cancellationToken)
	{
		string? inputError = ValidateFields(input);
		if (inputError is not null)
		{
			return ServiceResult<WorkCard>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		string? referenceError = await CardReferenceValidator.ValidateAsync(context, input, cancellationToken);
		if (referenceError is not null)
		{
			return ServiceResult<WorkCard>.Failure(referenceError);
		}

		CardLane entryLane = CardLifecycleRules.EntryLane;
		int? maxSortOrder = await context.Cards
			.Where(card => card.Lane == entryLane)
			.MaxAsync(card => (int?)card.SortOrder, cancellationToken);
		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

		WorkCard card = new()
		{
			Name = InputRules.Normalize(input.Name),
			Description = InputRules.Normalize(input.Description),
			Lane = entryLane,
			SortOrder = maxSortOrder is int max ? max + 1 : 0,
			CategoryId = input.CategoryId,
			WorkerAgentId = input.WorkerAgentId,
			ReviewerAgentId = input.ReviewerAgentId,
			AttemptLimit = input.AttemptLimit,
			CreatedUtc = now,
			UpdatedUtc = now,
		};

		// The insert is the transaction's first write, so it holds the write lock while the references are checked again: an agent change that committed after the first check is caught here, and one that commits later counts this card. A reference deleted meanwhile fails the insert's foreign key and is reported the same way.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		context.Cards.Add(card);
		try
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException)
		{
			context.ChangeTracker.Clear();
			return ServiceResult<WorkCard>.Failure(await CardReferenceValidator.ValidateAsync(context, input, cancellationToken) ?? throw new InvalidOperationException("The card could not be saved although its references are valid."));
		}

		if (await CardReferenceValidator.ValidateAsync(context, input, cancellationToken) is string changedReference)
		{
			return ServiceResult<WorkCard>.Failure(changedReference);
		}

		if (await MediaStore.ApplyEditAsync(context, card.Id, null, null, input.Media, now, cancellationToken) is string mediaError)
		{
			return ServiceResult<WorkCard>.Failure(mediaError);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<WorkCard>.Success(await LoadAsync(context, card.Id, cancellationToken));
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<WorkCard>> UpdateAsync(int id, CardInput input, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		var original = await context.Cards.AsNoTracking().Where(card => card.Id == id).Select(card => new { card.Lane, card.Version }).FirstOrDefaultAsync(cancellationToken);
		CardLane? lane = original?.Lane;
		int version = input.ExpectedVersion ?? original?.Version ?? -1;
		string? refusal = EditRefusal(lane, "edited");
		if (refusal is not null)
		{
			return ServiceResult<WorkCard>.Failure(refusal);
		}

		string? inputError = ValidateFields(input);
		if (inputError is not null)
		{
			return ServiceResult<WorkCard>.Failure(inputError);
		}

		string? referenceError = await CardReferenceValidator.ValidateAsync(context, input, cancellationToken);
		if (referenceError is not null)
		{
			return ServiceResult<WorkCard>.Failure(referenceError);
		}

		string name = InputRules.Normalize(input.Name);
		string description = InputRules.Normalize(input.Description);
		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

		int categoryId = input.CategoryId;
		int? workerId = input.WorkerAgentId;
		int? reviewerId = input.ReviewerAgentId;

		// The update is the transaction's first write, so it holds the write lock while the references are checked again (see CreateAsync). It only matches while the card is still in the lane that passed the check, so a card an agent picks up in between is never changed. A stopped card whose task changed starts again at work; saving it unchanged, or with only a new attempt limit, keeps the stage it resumes at.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		int updated;
		try
		{
			updated = await context.Cards.Where(card => card.Id == id && card.Lane == lane && card.Version == version).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.ResumeRound, card => card.Name == name && card.Description == description && card.CategoryId == categoryId && card.WorkerAgentId == workerId && card.ReviewerAgentId == reviewerId ? card.ResumeRound : null).SetProperty(card => card.Name, name).SetProperty(card => card.Description, description).SetProperty(card => card.CategoryId, categoryId).SetProperty(card => card.WorkerAgentId, workerId).SetProperty(card => card.ReviewerAgentId, reviewerId).SetProperty(card => card.AttemptLimit, input.AttemptLimit).SetProperty(card => card.Version, card => card.Version + 1).SetProperty(card => card.UpdatedUtc, now), cancellationToken);
		}
		catch (DbException)
		{
			await transaction.RollbackAsync(cancellationToken);
			return ServiceResult<WorkCard>.Failure(await CardReferenceValidator.ValidateAsync(context, input, cancellationToken) ?? throw new InvalidOperationException("The card could not be saved although its references are valid."));
		}

		if (updated == 0)
		{
			return ServiceResult<WorkCard>.Failure(EditRefusal(await FindLaneAsync(context, id, cancellationToken), "edited") ?? CardChanged);
		}

		if (await CardReferenceValidator.ValidateAsync(context, input, cancellationToken) is string changedReference)
		{
			return ServiceResult<WorkCard>.Failure(changedReference);
		}

		if (await MediaStore.ApplyEditAsync(context, id, id, version, input.Media, now, cancellationToken) is string mediaError)
		{
			return ServiceResult<WorkCard>.Failure(mediaError);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<WorkCard>.Success(await LoadAsync(context, id, cancellationToken));
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken, int? expectedVersion = null)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		var original = await context.Cards.AsNoTracking().Where(card => card.Id == id).Select(card => new { card.Lane, card.Version }).FirstOrDefaultAsync(cancellationToken);
		CardLane? lane = original?.Lane;
		int version = expectedVersion ?? original?.Version ?? -1;
		string? refusal = EditRefusal(lane, "deleted");
		if (refusal is not null)
		{
			return ServiceResult.Failure(refusal);
		}

		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		int deleted = await context.Cards.Where(card => card.Id == id && card.Lane == lane && card.Version == version).ExecuteDeleteAsync(cancellationToken);
		if (deleted == 0)
		{
			return ServiceResult.Failure(EditRefusal(await FindLaneAsync(context, id, cancellationToken), "deleted") ?? CardChanged);
		}

		await ResequenceLaneAsync(context, lane, cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> RetryAsync(int id, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		var card = await context.Cards
			.AsNoTracking()
			.Where(existing => existing.Id == id)
			.Select(existing => new { existing.Lane, existing.State, existing.Version })
			.FirstOrDefaultAsync(cancellationToken);

		if (card is null)
		{
			return ServiceResult.Failure(CardNotFound);
		}

		if (!CardLifecycleRules.CanRetry(card.Lane, card.State))
		{
			return ServiceResult.Failure(NotStoppedMessage);
		}

		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

		// The write only matches while the card is still the stopped card that passed the check. The next claim starts attempt 1 of a new run.
		int retried = await context.Cards.Where(existing => existing.Id == id && existing.Lane == card.Lane && existing.State == CardState.Error && existing.Version == card.Version).ExecuteUpdateAsync(changes => changes.SetProperty(existing => existing.State, CardState.Ready).SetProperty(existing => existing.Attempt, 0).SetProperty(existing => existing.Version, existing => existing.Version + 1).SetProperty(existing => existing.UpdatedUtc, now), cancellationToken);

		if (retried == 0)
		{
			var current = await context.Cards
				.AsNoTracking()
				.Where(existing => existing.Id == id)
				.Select(existing => new { existing.Lane, existing.State })
				.FirstOrDefaultAsync(cancellationToken);

			return ServiceResult.Failure(current is null ? CardNotFound : CardLifecycleRules.CanRetry(current.Lane, current.State) ? CardChanged : NotStoppedMessage);
		}

		return ServiceResult.Success();
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		CardLane? lane = await FindLaneAsync(context, id, cancellationToken);
		string? refusal = CloseRefusal(lane);
		if (refusal is not null)
		{
			return ServiceResult.Failure(refusal);
		}

		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

		// The close only matches while the card is still in the lane that passed the check, so a card an agent moves in between is never closed.
		int closed = await CloseCompletedAsync(context.Cards.Where(card => card.Id == id), now, cancellationToken);

		if (closed == 0)
		{
			return ServiceResult.Failure(CloseRefusal(await FindLaneAsync(context, id, cancellationToken)) ?? CardChanged);
		}

		await ResequenceLaneAsync(context, lane, cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	/// <inheritdoc/>
	public async Task<int> CloseCompletedAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await CloseCompletedAsync(context.Cards, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<CompletedCard>> GetCompletedAsync(DateTime fromUtc, DateTime untilUtc, int offset, int count, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await (from round in context.Rounds
					  join card in context.Cards on round.CardId equals card.Id
					  where round.Kind == RoundKind.Merge && round.Outcome == RoundOutcome.Merged && round.EndedUtc >= fromUtc && round.EndedUtc < untilUtc
					  orderby round.EndedUtc descending, card.Id
					  select new CompletedCard(card.Id, card.Name, card.Lane, round.EndedUtc.GetValueOrDefault())).Skip(offset).Take(count).ToListAsync(cancellationToken);
	}

	private static Task<int> CloseCompletedAsync(IQueryable<WorkCard> selection, DateTime now, CancellationToken cancellationToken)
	{
		return selection.Where(card => card.Lane == CardLane.Complete).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.Lane, CardLane.Closed).SetProperty(card => card.ClosedUtc, now).SetProperty(card => card.Version, card => card.Version + 1).SetProperty(card => card.UpdatedUtc, now), cancellationToken);
	}

	private static string? ValidateFields(CardInput input)
	{
		return CardInputRules.ValidateName(input.Name) ?? CardInputRules.ValidateDescription(input.Description) ?? CardInputRules.ValidateCategory(input.CategoryId) ?? CardInputRules.ValidateWorker(input.WorkerAgentId) ?? CardInputRules.ValidateAttemptLimit(input.AttemptLimit);
	}

	// The refusal for a person editing or deleting a card in the given lane, or null when the card exists and is in a lane people may change.
	private static string? EditRefusal(CardLane? lane, string action)
	{
		return lane switch
		{
			null => CardNotFound,
			CardLane current when !CardLifecycleRules.CanEdit(current) => LockedMessage(current, action),
			_ => null,
		};
	}

	// The refusal for a person closing a card in the given lane, or null when the card exists and is complete. Already closed is reported before the lane rule.
	private static string? CloseRefusal(CardLane? lane)
	{
		return lane switch
		{
			null => CardNotFound,
			CardLane.Closed => "Card is already closed.",
			CardLane current when !CardLifecycleRules.CanClose(current) => "Only completed cards can be closed.",
			_ => null,
		};
	}

	private static string LockedMessage(CardLane lane, string action)
	{
		string where = lane == CardLane.Closed ? "closed" : $"in {lane.DisplayName()}";

		return $"This card is {where} and can no longer be {action}.";
	}

	private static async Task<CardLane?> FindLaneAsync(ProjectDbContext context, int id, CancellationToken cancellationToken)
	{
		return await context.Cards
			.AsNoTracking()
			.Where(card => card.Id == id)
			.Select(card => (CardLane?)card.Lane)
			.FirstOrDefaultAsync(cancellationToken);
	}

	// Run inside the transaction that removed a card from the lane, so the lane never shows a gap.
	private static async Task ResequenceLaneAsync(ProjectDbContext context, CardLane? lane, CancellationToken cancellationToken)
	{
		List<WorkCard> remaining = await context.Cards
			.Where(card => card.Lane == lane)
			.OrderBy(card => card.SortOrder)
			.ThenBy(card => card.Id)
			.ToListAsync(cancellationToken);

		LaneSequencer.Resequence(remaining);
		await context.SaveChangesAsync(cancellationToken);
	}

	private static IQueryable<WorkCard> QueryWithReferences(ProjectDbContext context)
	{
		return context.Cards
			.AsNoTracking()
			.Include(card => card.Category)
			.Include(card => card.WorkerAgent)
			.Include(card => card.ReviewerAgent);
	}

	private static async Task<WorkCard> LoadAsync(ProjectDbContext context, int id, CancellationToken cancellationToken)
	{
		return await QueryWithReferences(context).FirstAsync(card => card.Id == id, cancellationToken);
	}
}
