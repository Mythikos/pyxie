using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The gate service with its creates counted and holdable by a test; every operation does what the real service does.</summary>
internal sealed class GatedGateService : IGateService
{
	private readonly IGateService _inner;

	/// <summary>Initializes a new instance of the <see cref="GatedGateService"/> class.</summary>
	/// <param name="inner">The service that does the work.</param>
	public GatedGateService(IGateService inner)
	{
		_inner = inner;
	}

	/// <summary>Gets the probe that counts and holds the creates of gates.</summary>
	public CallProbe Creates { get; } = new();

	/// <inheritdoc/>
	public Task<IReadOnlyList<Gate>> ListAsync(CancellationToken cancellationToken)
	{
		return _inner.ListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Gate>> CreateAsync(GateInput input, CancellationToken cancellationToken)
	{
		await Creates.EnterAsync(cancellationToken);

		return await _inner.CreateAsync(input, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Gate>> UpdateAsync(int id, GateInput input, CancellationToken cancellationToken) => UpdateIfCurrentAsync(id, input, null, cancellationToken);

	/// <inheritdoc/>
	public Task<ServiceResult<Gate>> UpdateIfCurrentAsync(int id, GateInput input, string? expectedRevision, CancellationToken cancellationToken)
	{
		return _inner.UpdateIfCurrentAsync(id, input, expectedRevision, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken) => DeleteIfCurrentAsync(id, null, cancellationToken);

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken)
	{
		return _inner.DeleteIfCurrentAsync(id, expectedRevision, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> MoveAsync(int id, int position, CancellationToken cancellationToken)
	{
		return _inner.MoveAsync(id, position, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> MoveIfOrderMatchesAsync(int id, int position, IReadOnlyList<int>? expectedOrder, CancellationToken cancellationToken) => _inner.MoveIfOrderMatchesAsync(id, position, expectedOrder, cancellationToken);
}
