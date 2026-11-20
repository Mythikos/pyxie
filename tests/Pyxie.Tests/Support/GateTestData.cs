using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Writes and reads gates at exact positions, so tests can arrange the gaps and ties that resequencing must repair, which <see cref="TestData.AddGateAsync"/> never produces.</summary>
public static class GateTestData
{
	/// <summary>Adds a gate at an exact position, whatever positions the other gates have.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="name">The gate name; unique across gates.</param>
	/// <param name="sortOrder">The position to store.</param>
	/// <param name="isEnabled">Whether gate rounds run it.</param>
	/// <param name="command">The command; <c>dotnet</c> unless given.</param>
	/// <param name="arguments">The arguments; <c>test</c> unless given.</param>
	/// <param name="timeoutMinutes">The timeout; the default unless given.</param>
	/// <returns>The stored gate.</returns>
	public static async Task<Gate> AddAtAsync(TestDatabase database, string name, int sortOrder, bool isEnabled = true, string command = "dotnet", string arguments = "test", int timeoutMinutes = PyxieLimits.DefaultGateTimeoutMinutes)
	{
		await using ProjectDbContext context = database.CreateContext();
		Gate gate = new()
		{
			Name = name,
			Command = command,
			Arguments = arguments,
			TimeoutMinutes = timeoutMinutes,
			SortOrder = sortOrder,
			IsEnabled = isEnabled,
		};

		context.Gates.Add(gate);
		await context.SaveChangesAsync(TestContext.Current.CancellationToken);

		return gate;
	}

	/// <summary>Adds gates at consecutive positions starting from zero, in the order given.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="names">The gate names, in the order they run.</param>
	/// <returns>The stored gates keyed by name.</returns>
	public static async Task<Dictionary<string, Gate>> AddOrderedAsync(TestDatabase database, params string[] names)
	{
		Dictionary<string, Gate> gates = [];
		for (int position = 0; position < names.Length; position++)
		{
			gates[names[position]] = await AddAtAsync(database, names[position], position);
		}

		return gates;
	}

	/// <summary>Reads every gate without tracking, in the order gates run.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The gates ordered by position, then identifier.</returns>
	public static async Task<List<Gate>> ReadAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Gates
			.AsNoTracking()
			.OrderBy(gate => gate.SortOrder)
			.ThenBy(gate => gate.Id)
			.ToListAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads the gate names in the order gates run, joined with commas.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The names in position order.</returns>
	public static async Task<string> ReadNamesAsync(TestDatabase database)
	{
		List<Gate> gates = await ReadAsync(database);

		return string.Join(",", gates.Select(gate => gate.Name));
	}

	/// <summary>Reads the stored positions of the gates in the order gates run, joined with commas.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The stored positions.</returns>
	public static async Task<string> ReadPositionsAsync(TestDatabase database)
	{
		List<Gate> gates = await ReadAsync(database);

		return string.Join(",", gates.Select(gate => gate.SortOrder));
	}
}
