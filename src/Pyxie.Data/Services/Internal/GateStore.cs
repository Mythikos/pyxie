using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Data.Services.Internal;

/// <summary>Owns how gates are written within a caller's context: what a gate's fields become, the order gates run in, and how positions are renumbered. <see cref="GateService"/> and the settings import both write gates through it.</summary>
internal static class GateStore
{
	/// <summary>Gives a gate the fields of an input. Its position is not an input.</summary>
	/// <param name="gate">The gate, new or tracked.</param>
	/// <param name="input">The input.</param>
	public static void Apply(Gate gate, GateInput input)
	{
		gate.Name = InputRules.Normalize(input.Name);
		gate.Command = InputRules.Normalize(input.Command);
		gate.Arguments = InputRules.Normalize(input.Arguments);
		gate.TimeoutMinutes = input.TimeoutMinutes;
		gate.IsEnabled = input.IsEnabled;
	}

	/// <summary>Loads every gate, tracked, in the order gates run, so renumbering writes only the gates whose position changes. Older writes may have left gaps or ties, and the id settles a tie as <see cref="GateService.ListAsync"/> does.</summary>
	/// <param name="context">The context.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The gates in run order.</returns>
	public static async Task<List<Gate>> LoadInOrderAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		return await context.Gates
			.OrderBy(gate => gate.SortOrder)
			.ThenBy(gate => gate.Id)
			.ToListAsync(cancellationToken);
	}

	/// <summary>Numbers gates by their place in a list, from zero, so positions stay consecutive.</summary>
	/// <param name="gates">The gates in the order they run.</param>
	public static void Renumber(IReadOnlyList<Gate> gates)
	{
		for (int position = 0; position < gates.Count; position++)
		{
			gates[position].SortOrder = position;
		}
	}
}
