using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="IGateService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class GateService : IGateService
{
	private const string NotFoundMessage = "Gate not found.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;

	/// <summary>Initializes a new instance of the <see cref="GateService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	public GateService(IDbContextFactory<ProjectDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<Gate>> ListAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Gates
			.AsNoTracking()
			.OrderBy(gate => gate.SortOrder)
			.ThenBy(gate => gate.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Gate>> CreateAsync(GateInput input, CancellationToken cancellationToken)
	{
		string? inputError = GateInputRules.Validate(input);
		if (inputError is not null)
		{
			return ServiceResult<Gate>.Failure(inputError);
		}

		string name = InputRules.Normalize(input.Name);
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (await IsNameTakenAsync(context, name, null, cancellationToken))
		{
			return ServiceResult<Gate>.Failure(DuplicateMessage(name));
		}

		// One past the highest position rather than the count, so a gap left by an older write never puts the new gate among existing ones.
		int? lastPosition = await context.Gates.MaxAsync(existing => (int?)existing.SortOrder, cancellationToken);
		Gate gate = new() { SortOrder = (lastPosition ?? -1) + 1 };
		GateStore.Apply(gate, input);
		context.Gates.Add(gate);
		if (!await TrySaveAsync(context, name, null, cancellationToken))
		{
			return ServiceResult<Gate>.Failure(DuplicateMessage(name));
		}

		return ServiceResult<Gate>.Success(gate);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Gate>> UpdateAsync(int id, GateInput input, CancellationToken cancellationToken) => UpdateIfCurrentAsync(id, input, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult<Gate>> UpdateIfCurrentAsync(int id, GateInput input, string? expectedRevision, CancellationToken cancellationToken)
	{
		string? inputError = GateInputRules.Validate(input);
		if (inputError is not null)
		{
			return ServiceResult<Gate>.Failure(inputError);
		}

		string name = InputRules.Normalize(input.Name);
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Gate? gate = await context.Gates.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (gate is null)
		{
			return ServiceResult<Gate>.Failure(NotFoundMessage);
		}

		if (expectedRevision is not null && expectedRevision != SettingsRevision.Of(gate))
		{
			return ServiceResult<Gate>.Failure(SettingsRevision.ChangedMessage);
		}

		if (await IsNameTakenAsync(context, name, id, cancellationToken))
		{
			return ServiceResult<Gate>.Failure(DuplicateMessage(name));
		}

		GateStore.Apply(gate, input);
		if (!await TrySaveAsync(context, name, id, cancellationToken))
		{
			return ServiceResult<Gate>.Failure(DuplicateMessage(name));
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<Gate>.Success(gate);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken) => DeleteIfCurrentAsync(id, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		List<Gate> gates = await GateStore.LoadInOrderAsync(context, cancellationToken);
		Gate? gate = gates.Find(existing => existing.Id == id);
		if (gate is null)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		if (expectedRevision is not null && expectedRevision != SettingsRevision.Of(gate))
		{
			return ServiceResult.Failure(SettingsRevision.ChangedMessage);
		}

		// The removal and the renumbering are one save, so a failure leaves the gate and the order as they were.
		gates.Remove(gate);
		context.Gates.Remove(gate);
		GateStore.Renumber(gates);
		await context.SaveChangesAsync(cancellationToken);

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	/// <inheritdoc/>
	public Task<ServiceResult> MoveAsync(int id, int position, CancellationToken cancellationToken) => MoveIfOrderMatchesAsync(id, position, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult> MoveIfOrderMatchesAsync(int id, int position, IReadOnlyList<int>? expectedOrder, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		List<Gate> gates = await GateStore.LoadInOrderAsync(context, cancellationToken);
		Gate? gate = gates.Find(existing => existing.Id == id);
		if (gate is null)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		if (expectedOrder is not null && !gates.Select(item => item.Id).SequenceEqual(expectedOrder))
		{
			return ServiceResult.Failure("The gate order changed. Read the ordered gates before moving this one.");
		}

		// Without the gate, the list has every position it can be moved to: the first, each gap between gates, and the last.
		gates.Remove(gate);
		gates.Insert(Math.Clamp(position, 0, gates.Count), gate);
		GateStore.Renumber(gates);
		await context.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	private static string DuplicateMessage(string name)
	{
		return $"A gate named '{name}' already exists.";
	}

	// A concurrent writer can win between the name check and the save; the unique index then rejects this write, which is reported as a duplicate.
	private static async Task<bool> TrySaveAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		try
		{
			await context.SaveChangesAsync(cancellationToken);

			return true;
		}
		catch (DbUpdateException)
		{
			if (await IsNameTakenAsync(context, name, excludedId, cancellationToken))
			{
				return false;
			}

			throw;
		}
	}

	private static async Task<bool> IsNameTakenAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		List<string> otherNames = await context.Gates
			.AsNoTracking()
			.Where(gate => gate.Id != excludedId)
			.Select(gate => gate.Name)
			.ToListAsync(cancellationToken);

		return InputRules.ContainsName(otherNames, name);
	}
}
