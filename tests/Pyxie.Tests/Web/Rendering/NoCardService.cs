using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A card service of a project that has no cards, for components that only ask whether a card exists, such as the list of branches, which names the card of a card branch: every card is reported missing, and nothing can be changed.</summary>
internal sealed class NoCardService : ICardService
{
	private const string NoCards = "The project has no cards.";

	/// <inheritdoc/>
	public Task<IReadOnlyList<BoardCard>> GetBoardAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<BoardCard>>([]);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<IReadOnlyList<CardRound>>> GetHistoryAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<IReadOnlyList<CardRound>>.Failure(NoCards));
	}

	/// <inheritdoc/>
	public Task<ServiceResult<WorkCard>> GetAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<WorkCard>.Failure(NoCards));
	}

	/// <inheritdoc/>
	public Task<ServiceResult<WorkCard>> CreateAsync(CardInput input, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<WorkCard>.Failure(NoCards));
	}

	/// <inheritdoc/>
	public Task<ServiceResult<WorkCard>> UpdateAsync(int id, CardInput input, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<WorkCard>.Failure(NoCards));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(NoCards));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> RetryAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(NoCards));
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Failure(NoCards));
	}
}
