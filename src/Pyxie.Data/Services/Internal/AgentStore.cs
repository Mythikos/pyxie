using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Rules;

namespace Pyxie.Data.Services.Internal;

/// <summary>Owns how agents are written within a caller's context and transaction: what an agent's fields become, how finished cards let go of an agent, which unfinished cards hold on to it, and the messages that refuse a change those cards forbid. <see cref="AgentService"/> and the settings import both write agents through it, so the two follow one set of rules.</summary>
internal static class AgentStore
{
	// Finished cards are the only cards that let go of an agent; every other card keeps the agents it was given.
	private static readonly CardLane[] s_finishedLanes = [.. CardLifecycleRules.FinishedLanes];

	/// <summary>Gives an agent the fields of an input.</summary>
	/// <param name="agent">The agent, new or tracked.</param>
	/// <param name="input">The input, normalized by <see cref="AgentInputRules.Normalize"/>.</param>
	public static void Apply(Agent agent, AgentInput input)
	{
		agent.Name = input.Name;
		agent.Harness = input.Harness;
		agent.Authentication = input.Authentication;
		agent.Command = input.Command;
		agent.Arguments = input.Arguments;
		agent.CanWork = input.CanWork;
		agent.CanReview = input.CanReview;
		agent.CanMerge = input.CanMerge;
	}

	/// <summary>Removes an agent from the given roles on every finished card (complete or closed), then counts the unfinished cards that still have it in those roles. An unfinished card keeps its agents: every card needs its worker until it is merged, and clearing a reviewer would silently skip its review. Rounds already run keep their own copy of the agent. The release is the transaction's first write, so it takes the database's write lock (on SQL Server, the locks of the rows it changes) before the count, and a pipeline write racing it fails its version check and re-reads. The caller rolls back when any count is not zero.</summary>
	/// <param name="context">The context, inside the caller's transaction.</param>
	/// <param name="agentId">The agent.</param>
	/// <param name="releaseWorker">Whether the worker role is released.</param>
	/// <param name="releaseReviewer">Whether the reviewer role is released.</param>
	/// <param name="now">The time the released cards are stamped with.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The unfinished cards that keep the agent as worker, as reviewer, and in either role (a card holding it in both roles counts once).</returns>
	public static async Task<(int Working, int Reviewing, int Cards)> ReleaseAsync(ProjectDbContext context, int agentId, bool releaseWorker, bool releaseReviewer, DateTime now, CancellationToken cancellationToken)
	{
		CardLane[] finishedLanes = s_finishedLanes;

		// One statement clears both roles, so a card advances its version once even when the agent held both.
		await context.Cards.Where(card => finishedLanes.Contains(card.Lane) && ((releaseWorker && card.WorkerAgentId == agentId) || (releaseReviewer && card.ReviewerAgentId == agentId))).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.WorkerAgentId, card => releaseWorker && card.WorkerAgentId == agentId ? null : card.WorkerAgentId).SetProperty(card => card.ReviewerAgentId, card => releaseReviewer && card.ReviewerAgentId == agentId ? null : card.ReviewerAgentId).SetProperty(card => card.Version, card => card.Version + 1).SetProperty(card => card.UpdatedUtc, now), cancellationToken);

		return await CountUnfinishedAsync(context, agentId, releaseWorker, releaseReviewer, cancellationToken);
	}

	/// <summary>Counts the unfinished cards that have an agent in the given roles, without changing anything.</summary>
	/// <param name="context">The context.</param>
	/// <param name="agentId">The agent.</param>
	/// <param name="worker">Whether the worker role is counted.</param>
	/// <param name="reviewer">Whether the reviewer role is counted.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The unfinished cards that have the agent as worker, as reviewer, and in either role (a card holding it in both roles counts once).</returns>
	public static async Task<(int Working, int Reviewing, int Cards)> CountUnfinishedAsync(ProjectDbContext context, int agentId, bool worker, bool reviewer, CancellationToken cancellationToken)
	{
		CardLane[] finishedLanes = s_finishedLanes;

		int working = worker ? await context.Cards.CountAsync(card => card.WorkerAgentId == agentId && !finishedLanes.Contains(card.Lane), cancellationToken) : 0;
		int reviewing = reviewer ? await context.Cards.CountAsync(card => card.ReviewerAgentId == agentId && !finishedLanes.Contains(card.Lane), cancellationToken) : 0;
		int cards = await context.Cards.CountAsync(card => !finishedLanes.Contains(card.Lane) && ((worker && card.WorkerAgentId == agentId) || (reviewer && card.ReviewerAgentId == agentId)), cancellationToken);

		return (working, reviewing, cards);
	}

	/// <summary>Clears the merge role from every agent but one, as one statement. Run before any tracked write that gives an agent the role, so the unique index on the role never sees two merge agents, whatever order the writes are sent in.</summary>
	/// <param name="context">The context, inside the caller's transaction.</param>
	/// <param name="keptAgentId">The agent that keeps the role if it has it, or <see langword="null"/> to clear it from every agent.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the role is cleared.</returns>
	public static Task<int> ClearMergeRoleExceptAsync(ProjectDbContext context, int? keptAgentId, CancellationToken cancellationToken)
	{
		return context.Agents.Where(agent => agent.CanMerge && agent.Id != keptAgentId).ExecuteUpdateAsync(changes => changes.SetProperty(agent => agent.CanMerge, false), cancellationToken);
	}

	/// <summary>Gets the message that refuses taking the worker role from an agent that unfinished cards have as their worker.</summary>
	/// <param name="name">The agent's name.</param>
	/// <param name="working">The unfinished cards that have it as worker.</param>
	/// <returns>The message.</returns>
	public static string KeepWorkerRefusal(string name, int working)
	{
		return $"Agent '{name}' is the worker of {UnfinishedCards(working)} and must keep the worker role.";
	}

	/// <summary>Gets the message that refuses taking the reviewer role from an agent that unfinished cards have as their reviewer.</summary>
	/// <param name="name">The agent's name.</param>
	/// <param name="reviewing">The unfinished cards that have it as reviewer.</param>
	/// <returns>The message.</returns>
	public static string KeepReviewerRefusal(string name, int reviewing)
	{
		return $"Agent '{name}' is the reviewer of {UnfinishedCards(reviewing)} and must keep the reviewer role.";
	}

	/// <summary>Gets the message that refuses deleting an agent that unfinished cards use.</summary>
	/// <param name="name">The agent's name.</param>
	/// <param name="working">The unfinished cards that have it as worker.</param>
	/// <param name="reviewing">The unfinished cards that have it as reviewer.</param>
	/// <param name="cards">The unfinished cards that have it in either role.</param>
	/// <returns>The message.</returns>
	public static string DeleteRefusal(string name, int working, int reviewing, int cards)
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

		return $"Agent '{name}' is assigned to {UnfinishedCards(cards)} ({string.Join(", ", parts)}) and cannot be deleted.";
	}

	private static string UnfinishedCards(int count)
	{
		return count == 1 ? "1 card that is not complete" : $"{count} cards that are not complete";
	}
}
