using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Manages the agent definitions available to cards.</summary>
public interface IAgentService
{
	/// <summary>Gets all agents ordered by name.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The agents.</returns>
	Task<IReadOnlyList<Agent>> ListAsync(CancellationToken cancellationToken);

	/// <summary>Creates an agent.</summary>
	/// <param name="input">The agent fields.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The created agent, or a failure describing why the input was rejected. At most one agent of the project resolves merge conflicts (<see cref="AgentInput.CanMerge"/>): creating a second is refused with the name of the first, also when another caller creates it at the same moment, and then nothing is created.</returns>
	Task<ServiceResult<Agent>> CreateAsync(AgentInput input, CancellationToken cancellationToken);

	/// <summary>Updates an agent.</summary>
	/// <param name="id">The agent identifier.</param>
	/// <param name="input">The new agent fields.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The updated agent, or a failure describing why the change was rejected. Dropping a role is refused while the agent has that role on a card that is not complete; otherwise that role is cleared from every complete and closed card using it, and each such card's version advances once. Giving the merge role to an agent while another agent has it is refused with that agent's name, also when another caller gets there first; an agent that already has the role keeps it, and dropping the role is never refused, because conflicts then go back to the worker. A refused change leaves nothing changed.</returns>
	Task<ServiceResult<Agent>> UpdateAsync(int id, AgentInput input, CancellationToken cancellationToken);

	/// <summary>Applies this operation only while the item still has the definition the caller read.</summary>
	/// <param name="id">The item identifier.</param>
	/// <param name="input">The replacement fields.</param>
	/// <param name="expectedRevision">The revision from <see cref="SettingsRevision"/>, or null for an unconditional request.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The operation result, including a refusal if the item changed.</returns>
	Task<ServiceResult<Agent>> UpdateIfCurrentAsync(int id, AgentInput input, string? expectedRevision, CancellationToken cancellationToken);

	/// <summary>Makes an agent the project's only merge agent, or clears the merge role from every agent. Both changes are made together, so the project never has two merge agents and, when the agent is missing, never loses its current one.</summary>
	/// <param name="agentId">The agent that takes the merge role, or <see langword="null"/> to clear the role from every agent.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the agent is missing, in which case nothing is changed. Choosing the agent that already has the role succeeds and changes nothing. The role holds no card, so no card protects it (see <see cref="DeleteAsync"/>).</returns>
	Task<ServiceResult> SetMergeAgentAsync(int? agentId, CancellationToken cancellationToken);

	/// <summary>Deletes an agent and clears its references on complete and closed cards, unless a card that is not complete still uses it. A refused delete leaves nothing changed.</summary>
	/// <param name="id">The agent identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the agent is missing, or is the worker or reviewer of a card that is not complete. The merge agent is deleted like any other: the merge role holds no card. Otherwise each card that named the agent loses the reference and its version advances once.</returns>
	Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken);

	/// <summary>Applies this operation only while the item still has the definition the caller read.</summary>
	/// <param name="id">The item identifier.</param>
	/// <param name="expectedRevision">The revision from <see cref="SettingsRevision"/>, or null for an unconditional request.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The operation result, including a refusal if the item changed.</returns>
	Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken);
}
