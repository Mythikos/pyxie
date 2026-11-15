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
public sealed record AgentInput(string Name, HarnessKind Harness, string Command, string Arguments, bool CanWork, bool CanReview, bool CanMerge = false);
