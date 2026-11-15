using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The card service of the board with its reads of the board counted and holdable by a test; every operation does what the real service does.</summary>
internal sealed class GatedCardService : ICardService
{
	private readonly ICardService _inner;

	/// <summary>Initializes a new instance of the <see cref="GatedCardService"/> class.</summary>
	/// <param name="inner">The service that does the work.</param>
	public GatedCardService(ICardService inner)
	{
		_inner = inner;
	}

	/// <summary>Gets the probe that counts and holds the reads of the board.</summary>
	public CallProbe BoardReads { get; } = new();

	/// <inheritdoc/>
	public async Task<IReadOnlyList<BoardCard>> GetBoardAsync(CancellationToken cancellationToken)
	{
		await BoardReads.EnterAsync(cancellationToken);

		return await _inner.GetBoardAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<IReadOnlyList<CardRound>>> GetHistoryAsync(int id, CancellationToken cancellationToken)
	{
		return _inner.GetHistoryAsync(id, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<WorkCard>> GetAsync(int id, CancellationToken cancellationToken)
	{
		return _inner.GetAsync(id, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<WorkCard>> CreateAsync(CardInput input, CancellationToken cancellationToken)
	{
		return _inner.CreateAsync(input, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<WorkCard>> UpdateAsync(int id, CardInput input, CancellationToken cancellationToken)
	{
		return _inner.UpdateAsync(id, input, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken)
	{
		return _inner.DeleteAsync(id, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> RetryAsync(int id, CancellationToken cancellationToken)
	{
		return _inner.RetryAsync(id, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken)
	{
		return _inner.CloseAsync(id, cancellationToken);
	}
}
