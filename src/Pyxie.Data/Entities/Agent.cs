using Pyxie.Data.Enums;

namespace Pyxie.Data.Entities;

/// <summary>A local agent harness definition that can work on or review cards.</summary>
public class Agent
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the unique, case-insensitive display name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the harness the agent runs through.</summary>
	public HarnessKind Harness { get; set; }

	/// <summary>Gets or sets the executable used to start the harness.</summary>
	public string Command { get; set; } = string.Empty;

	/// <summary>Gets or sets the command-line arguments, empty when there are none. They can carry tokens, such as <c>{CARD_NAME}</c>, that each round fills in (<see cref="Services.Pipeline.AgentArgumentTokens"/>).</summary>
	public string Arguments { get; set; } = string.Empty;

	/// <summary>Gets or sets a value indicating whether the agent may be assigned as a card worker.</summary>
	public bool CanWork { get; set; }

	/// <summary>Gets or sets a value indicating whether the agent may be assigned as a card reviewer.</summary>
	public bool CanReview { get; set; }

	/// <summary>Gets or sets a value indicating whether the agent is the project's merge agent, which resolves the conflicts of merging the target branch into a card's branch. At most one agent of a project is.</summary>
	public bool CanMerge { get; set; }
}
