using Pyxie.Data.Enums;

namespace Pyxie.Data.Entities;

/// <summary>A copy of an agent definition as it was when a round started. History keeps what actually ran even after the agent is renamed, reconfigured, or deleted, and the pipeline invokes the harness from this copy so a round runs exactly what it recorded. The arguments are kept as configured, with their tokens (<see cref="Services.Pipeline.AgentArgumentTokens"/>) filled in only when the round runs.</summary>
public sealed class AgentSnapshot
{
	/// <summary>Initializes a new instance of the <see cref="AgentSnapshot"/> class.</summary>
	/// <param name="name">The agent name.</param>
	/// <param name="harness">The harness the agent runs through.</param>
	/// <param name="command">The executable used to start the harness.</param>
	/// <param name="arguments">The command-line arguments, empty when there are none.</param>
	public AgentSnapshot(string name, HarnessKind harness, string command, string arguments)
	{
		Name = name;
		Harness = harness;
		Command = command;
		Arguments = arguments;
	}

	/// <summary>Gets the agent name.</summary>
	public string Name { get; private set; }

	/// <summary>Gets the harness the agent runs through.</summary>
	public HarnessKind Harness { get; private set; }

	/// <summary>Gets the executable used to start the harness.</summary>
	public string Command { get; private set; }

	/// <summary>Gets the command-line arguments, empty when there are none.</summary>
	public string Arguments { get; private set; }

	/// <summary>Copies an agent definition.</summary>
	/// <param name="agent">The agent to copy.</param>
	/// <returns>A snapshot holding the agent's current values.</returns>
	public static AgentSnapshot From(Agent agent)
	{
		return new AgentSnapshot(agent.Name, agent.Harness, agent.Command, agent.Arguments);
	}
}
