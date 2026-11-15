using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Manages the gates: the commands a gate round runs, in order, in a card's workspace. The pipeline reads the gates each time a gate round runs, so changing them never touches cards. A gate's position is its place in that order. A new gate goes last, and deleting or moving a gate renumbers every gate from zero.</summary>
public interface IGateService
{
	/// <summary>Gets all gates in the order they run.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The gates ordered by position.</returns>
	Task<IReadOnlyList<Gate>> ListAsync(CancellationToken cancellationToken);

	/// <summary>Creates a gate at the end of the order.</summary>
	/// <param name="input">The gate fields.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The created gate, or a failure describing why the input was rejected.</returns>
	Task<ServiceResult<Gate>> CreateAsync(GateInput input, CancellationToken cancellationToken);

	/// <summary>Updates a gate's fields. Its position is unchanged.</summary>
	/// <param name="id">The gate identifier.</param>
	/// <param name="input">The new gate fields.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The updated gate, or a failure describing why the change was rejected.</returns>
	Task<ServiceResult<Gate>> UpdateAsync(int id, GateInput input, CancellationToken cancellationToken);

	/// <summary>Deletes a gate. The gates after it move up so positions stay consecutive, in the same transaction.</summary>
	/// <param name="id">The gate identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the gate is missing.</returns>
	Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken);

	/// <summary>Moves a gate to a position in the order gates run, and renumbers every gate from zero.</summary>
	/// <param name="id">The gate identifier.</param>
	/// <param name="position">The zero-based position to move the gate to. A position outside the gates is clamped to the first or last position.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the gate is missing.</returns>
	Task<ServiceResult> MoveAsync(int id, int position, CancellationToken cancellationToken);
}
