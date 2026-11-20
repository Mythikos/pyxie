using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="IAgentService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class AgentService : IAgentService
{
	private const string NotFoundMessage = "Agent not found.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="AgentService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	/// <param name="timeProvider">The clock that stamps the cards whose agent references are cleared.</param>
	public AgentService(IDbContextFactory<ProjectDbContext> contextFactory, TimeProvider timeProvider)
	{
		_contextFactory = contextFactory;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<Agent>> ListAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Agents
			.AsNoTracking()
			.OrderBy(agent => agent.Name)
			.ThenBy(agent => agent.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Agent>> CreateAsync(AgentInput input, CancellationToken cancellationToken)
	{
		AgentInput normalized = AgentInputRules.Normalize(input);
		if (AgentInputRules.Validate(normalized) is string inputError)
		{
			return ServiceResult<Agent>.Failure(inputError);
		}

		string name = normalized.Name;

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (await IsNameTakenAsync(context, name, null, cancellationToken))
		{
			return ServiceResult<Agent>.Failure(DuplicateMessage(name));
		}

		if (await MergeRefusalAsync(context, input.CanMerge, null, cancellationToken) is string refusal)
		{
			return ServiceResult<Agent>.Failure(refusal);
		}

		// Every return before the commit rolls back, so a refused agent leaves no row behind.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Agent agent = new();
		AgentStore.Apply(agent, normalized);
		context.Agents.Add(agent);
		if (await TrySaveAsync(context, name, input.CanMerge, null, cancellationToken) is string raced)
		{
			return ServiceResult<Agent>.Failure(raced);
		}

		// The check made before the transaction cannot stop two callers that pass it together, so it is made again after the write, where it sees what the other caller committed.
		if (await MergeRefusalAsync(context, input.CanMerge, agent.Id, cancellationToken) is string concurrent)
		{
			return ServiceResult<Agent>.Failure(concurrent);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<Agent>.Success(agent);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Agent>> UpdateAsync(int id, AgentInput input, CancellationToken cancellationToken) => UpdateIfCurrentAsync(id, input, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult<Agent>> UpdateIfCurrentAsync(int id, AgentInput input, string? expectedRevision, CancellationToken cancellationToken)
	{
		AgentInput normalized = AgentInputRules.Normalize(input);
		if (AgentInputRules.Validate(normalized) is string inputError)
		{
			return ServiceResult<Agent>.Failure(inputError);
		}

		string name = normalized.Name;

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Agent? agent = await context.Agents.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (agent is null)
		{
			return ServiceResult<Agent>.Failure(NotFoundMessage);
		}

		if (expectedRevision is not null && expectedRevision != SettingsRevision.Of(agent))
		{
			return ServiceResult<Agent>.Failure(SettingsRevision.ChangedMessage);
		}

		if (await IsNameTakenAsync(context, name, id, cancellationToken))
		{
			return ServiceResult<Agent>.Failure(DuplicateMessage(name));
		}

		if (await MergeRefusalAsync(context, input.CanMerge, id, cancellationToken) is string refusal)
		{
			return ServiceResult<Agent>.Failure(refusal);
		}

		if (!input.CanWork || !input.CanReview)
		{
			(int working, int reviewing, _) = await AgentStore.ReleaseAsync(context, id, releaseWorker: !input.CanWork, releaseReviewer: !input.CanReview, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
			if (working > 0)
			{
				return ServiceResult<Agent>.Failure(AgentStore.KeepWorkerRefusal(agent.Name, working));
			}

			if (reviewing > 0)
			{
				return ServiceResult<Agent>.Failure(AgentStore.KeepReviewerRefusal(agent.Name, reviewing));
			}
		}

		AgentStore.Apply(agent, normalized);
		if (await TrySaveAsync(context, name, input.CanMerge, id, cancellationToken) is string raced)
		{
			return ServiceResult<Agent>.Failure(raced);
		}

		// The check made before the transaction cannot stop two callers that pass it together, so it is made again after the write, where it sees what the other caller committed.
		if (await MergeRefusalAsync(context, input.CanMerge, id, cancellationToken) is string concurrent)
		{
			return ServiceResult<Agent>.Failure(concurrent);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<Agent>.Success(agent);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> SetMergeAgentAsync(int? agentId, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		// Every return before the commit rolls back, so a missing agent leaves the current merge agent in place.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		// The role is cleared first, as the transaction's first write, which takes the database's write lock: the unique index on the merge role checks each row as it is written, so the new merge agent can only be written once no other agent has the role, and no other caller can slip in between the two writes.
		await AgentStore.ClearMergeRoleExceptAsync(context, agentId, cancellationToken);

		if (agentId is int id && await context.Agents.Where(agent => agent.Id == id).ExecuteUpdateAsync(changes => changes.SetProperty(agent => agent.CanMerge, true), cancellationToken) == 0)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken) => DeleteIfCurrentAsync(id, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Agent? agent = await context.Agents.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (agent is null)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		if (expectedRevision is not null && expectedRevision != SettingsRevision.Of(agent))
		{
			return ServiceResult.Failure(SettingsRevision.ChangedMessage);
		}

		// The foreign keys would also set these to null; releasing explicitly keeps the behavior independent of provider FK enforcement and advances each card's version.
		(int working, int reviewing, int cards) = await AgentStore.ReleaseAsync(context, id, releaseWorker: true, releaseReviewer: true, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
		if (cards > 0)
		{
			return ServiceResult.Failure(AgentStore.DeleteRefusal(agent.Name, working, reviewing, cards));
		}

		context.Agents.Remove(agent);
		await context.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	private static string DuplicateMessage(string name)
	{
		return $"An agent named '{name}' already exists.";
	}

	// A concurrent writer can win between the checks and the save; a unique index (the name's, or the one that allows a single merge agent) then rejects this write, which is reported as the refusal it stands for. Any other failure is not ours to report.
	private static async Task<string?> TrySaveAsync(ProjectDbContext context, string name, bool canMerge, int? excludedId, CancellationToken cancellationToken)
	{
		try
		{
			await context.SaveChangesAsync(cancellationToken);

			return null;
		}
		catch (DbUpdateException)
		{
			if (await IsNameTakenAsync(context, name, excludedId, cancellationToken))
			{
				return DuplicateMessage(name);
			}

			if (await MergeRefusalAsync(context, canMerge, excludedId, cancellationToken) is string refusal)
			{
				return refusal;
			}

			throw;
		}
	}

	// At most one agent of the project resolves merge conflicts, because the pipeline sends every conflict to that one agent.
	private static async Task<string?> MergeRefusalAsync(ProjectDbContext context, bool canMerge, int? excludedId, CancellationToken cancellationToken)
	{
		if (!canMerge)
		{
			return null;
		}

		string? otherName = await context.Agents
			.AsNoTracking()
			.Where(agent => agent.CanMerge && agent.Id != excludedId)
			.OrderBy(agent => agent.Id)
			.Select(agent => agent.Name)
			.FirstOrDefaultAsync(cancellationToken);

		return otherName is null ? null : $"Agent '{otherName}' already resolves merge conflicts. Only one agent can.";
	}

	private static async Task<bool> IsNameTakenAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		List<string> otherNames = await context.Agents
			.AsNoTracking()
			.Where(agent => agent.Id != excludedId)
			.Select(agent => agent.Name)
			.ToListAsync(cancellationToken);

		return InputRules.ContainsName(otherNames, name);
	}
}
