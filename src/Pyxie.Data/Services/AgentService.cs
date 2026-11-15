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

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="IAgentService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class AgentService : IAgentService
{
	private const string NotFoundMessage = "Agent not found.";

	// Finished cards are the only cards that let go of an agent; every other card keeps the agents it was given.
	private static readonly CardLane[] s_finishedLanes = [.. CardLifecycleRules.FinishedLanes];

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
		string name = InputRules.Normalize(input.Name);
		string command = InputRules.Normalize(input.Command);
		string arguments = InputRules.Normalize(input.Arguments);
		string? inputError = ValidateFields(input, name, command, arguments);
		if (inputError is not null)
		{
			return ServiceResult<Agent>.Failure(inputError);
		}

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
		Apply(agent, input, name, command, arguments);
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
	public async Task<ServiceResult<Agent>> UpdateAsync(int id, AgentInput input, CancellationToken cancellationToken)
	{
		string name = InputRules.Normalize(input.Name);
		string command = InputRules.Normalize(input.Command);
		string arguments = InputRules.Normalize(input.Arguments);
		string? inputError = ValidateFields(input, name, command, arguments);
		if (inputError is not null)
		{
			return ServiceResult<Agent>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		Agent? agent = await context.Agents.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (agent is null)
		{
			return ServiceResult<Agent>.Failure(NotFoundMessage);
		}

		if (await IsNameTakenAsync(context, name, id, cancellationToken))
		{
			return ServiceResult<Agent>.Failure(DuplicateMessage(name));
		}

		if (await MergeRefusalAsync(context, input.CanMerge, id, cancellationToken) is string refusal)
		{
			return ServiceResult<Agent>.Failure(refusal);
		}

		// Every return before the commit rolls back, so a refused change leaves no card cleared.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		if (!input.CanWork || !input.CanReview)
		{
			(int working, int reviewing, _) = await ReleaseAsync(context, id, releaseWorker: !input.CanWork, releaseReviewer: !input.CanReview, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
			if (working > 0)
			{
				return ServiceResult<Agent>.Failure($"Agent '{agent.Name}' is the worker of {UnfinishedCards(working)} and must keep the worker role.");
			}

			if (reviewing > 0)
			{
				return ServiceResult<Agent>.Failure($"Agent '{agent.Name}' is the reviewer of {UnfinishedCards(reviewing)} and must keep the reviewer role.");
			}
		}

		Apply(agent, input, name, command, arguments);
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
		await context.Agents.Where(agent => agent.CanMerge && agent.Id != agentId).ExecuteUpdateAsync(changes => changes.SetProperty(agent => agent.CanMerge, false), cancellationToken);

		if (agentId is int id && await context.Agents.Where(agent => agent.Id == id).ExecuteUpdateAsync(changes => changes.SetProperty(agent => agent.CanMerge, true), cancellationToken) == 0)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		Agent? agent = await context.Agents.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (agent is null)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		// Every return before the commit rolls back, so a refused delete leaves no card cleared.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		// The foreign keys would also set these to null; releasing explicitly keeps the behavior independent of provider FK enforcement and advances each card's version.
		(int working, int reviewing, int cards) = await ReleaseAsync(context, id, releaseWorker: true, releaseReviewer: true, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
		if (cards > 0)
		{
			List<string> parts = [];
			if (working > 0)
			{
				parts.Add($"{working} as worker");
			}

			if (reviewing > 0)
			{
				parts.Add($"{reviewing} as reviewer");
			}

			return ServiceResult.Failure($"Agent '{agent.Name}' is assigned to {UnfinishedCards(cards)} ({string.Join(", ", parts)}) and cannot be deleted.");
		}

		context.Agents.Remove(agent);
		await context.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	private static string? ValidateFields(AgentInput input, string name, string command, string arguments)
	{
		if (!Enum.IsDefined(input.Harness))
		{
			return "Harness is not recognized.";
		}

		return InputRules.ValidateRequired(name, "Agent name", PyxieLimits.MaxNameLength) ?? InputRules.ValidateMaxLength(command, "Command", PyxieLimits.MaxCommandLength) ?? AgentInputRules.ValidateArguments(arguments);
	}

	private static void Apply(Agent agent, AgentInput input, string name, string command, string arguments)
	{
		agent.Name = name;
		agent.Harness = input.Harness;
		agent.Command = command;
		agent.Arguments = arguments;
		agent.CanWork = input.CanWork;
		agent.CanReview = input.CanReview;
		agent.CanMerge = input.CanMerge;
	}

	private static string UnfinishedCards(int count)
	{
		return count == 1 ? "1 card that is not complete" : $"{count} cards that are not complete";
	}

	/// <summary>Removes an agent from the given roles on every finished card (complete or closed), then counts the unfinished cards that still have it in those roles. An unfinished card keeps its agents: every card needs its worker until it is merged, and clearing a reviewer would silently skip its review. Rounds already run keep their own copy of the agent. The release is the transaction's first write, so it takes the database's write lock (on SQL Server, the locks of the rows it changes) before the count, and a pipeline write racing it fails its version check and re-reads. The caller rolls back when any count is not zero.</summary>
	/// <returns>The unfinished cards that keep the agent as worker, as reviewer, and in either role (a card holding it in both roles counts once).</returns>
	private static async Task<(int Working, int Reviewing, int Cards)> ReleaseAsync(ProjectDbContext context, int agentId, bool releaseWorker, bool releaseReviewer, DateTime now, CancellationToken cancellationToken)
	{
		CardLane[] finishedLanes = s_finishedLanes;

		// One statement clears both roles, so a card advances its version once even when the agent held both.
		await context.Cards.Where(card => finishedLanes.Contains(card.Lane) && ((releaseWorker && card.WorkerAgentId == agentId) || (releaseReviewer && card.ReviewerAgentId == agentId))).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.WorkerAgentId, card => releaseWorker && card.WorkerAgentId == agentId ? null : card.WorkerAgentId).SetProperty(card => card.ReviewerAgentId, card => releaseReviewer && card.ReviewerAgentId == agentId ? null : card.ReviewerAgentId).SetProperty(card => card.Version, card => card.Version + 1).SetProperty(card => card.UpdatedUtc, now), cancellationToken);

		int working = releaseWorker ? await context.Cards.CountAsync(card => card.WorkerAgentId == agentId && !finishedLanes.Contains(card.Lane), cancellationToken) : 0;
		int reviewing = releaseReviewer ? await context.Cards.CountAsync(card => card.ReviewerAgentId == agentId && !finishedLanes.Contains(card.Lane), cancellationToken) : 0;
		int cards = await context.Cards.CountAsync(card => !finishedLanes.Contains(card.Lane) && ((releaseWorker && card.WorkerAgentId == agentId) || (releaseReviewer && card.ReviewerAgentId == agentId)), cancellationToken);

		return (working, reviewing, cards);
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
