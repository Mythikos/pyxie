using Pyxie.Data.Entities;
using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The editable fields of an agent definition.</summary>
/// <param name="Name">The unique display name.</param>
/// <param name="Harness">The harness the agent runs through.</param>
/// <param name="Command">The executable used to start the harness.</param>
/// <param name="Arguments">The command-line arguments; empty when there are none.</param>
/// <param name="CanWork">Whether the agent may be assigned as a worker.</param>
/// <param name="CanReview">Whether the agent may be assigned as a reviewer.</param>
/// <param name="CanMerge">Whether the agent is the project's merge agent; at most one agent can be.</param>
public sealed record AgentInput(string Name, HarnessKind Harness, string Command, string Arguments, bool CanWork, bool CanReview, bool CanMerge = false)
{
	/// <summary>Gets the selected credential, or None for a custom harness.</summary>
	public AgentAuthentication Authentication { get; init; } = Harness == HarnessKind.Other ? AgentAuthentication.None : AgentAuthentication.Account;

	/// <summary>Gets the fields an agent has now.</summary>
	/// <param name="agent">The agent.</param>
	/// <returns>The agent's fields.</returns>
	public static AgentInput From(Agent agent)
	{
		return new AgentInput(agent.Name, agent.Harness, agent.Command, agent.Arguments, agent.CanWork, agent.CanReview, agent.CanMerge) { Authentication = agent.Authentication };
	}
}
