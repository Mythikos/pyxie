using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="IRepositoryService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class RepositoryService : IRepositoryService
{
	private const string AlreadyConnectedMessage = "A repository is already connected.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="RepositoryService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	/// <param name="timeProvider">The clock that stamps the time the repository was imported.</param>
	public RepositoryService(IDbContextFactory<ProjectDbContext> contextFactory, TimeProvider timeProvider)
	{
		_contextFactory = contextFactory;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<Repository?> GetAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Repositories
			.AsNoTracking()
			.OrderBy(repository => repository.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Repository>> ConnectAsync(RepositoryInput input, CancellationToken cancellationToken)
	{
		string? inputError = ValidateFields(input);
		if (inputError is not null)
		{
			return ServiceResult<Repository>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (await context.Repositories.AnyAsync(cancellationToken))
		{
			return ServiceResult<Repository>.Failure(AlreadyConnectedMessage);
		}

		// The check above cannot stop two callers that pass it together. So the insert comes first and the count after it, inside one transaction: the insert takes the database's write lock, so a second connect waits for the first to commit, then finds its row and rolls back. Every return before the commit rolls back.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Repository repository = new()
		{
			Source = input.Source,
			Path = InputRules.Normalize(input.Path),
			Origin = InputRules.Normalize(input.Origin),
			TargetBranch = InputRules.Normalize(input.TargetBranch),
			ImportedUtc = _timeProvider.GetUtcNow().UtcDateTime,
		};
		context.Repositories.Add(repository);
		await context.SaveChangesAsync(cancellationToken);

		if (await context.Repositories.CountAsync(cancellationToken) > 1)
		{
			return ServiceResult<Repository>.Failure(AlreadyConnectedMessage);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<Repository>.Success(repository);
	}

	private static string? ValidateFields(RepositoryInput input)
	{
		return RepositoryInputRules.ValidateSource(input.Source) ?? RepositoryInputRules.ValidatePath(input.Path) ?? RepositoryInputRules.ValidateOrigin(input.Source, input.Origin) ?? RepositoryInputRules.ValidateTargetBranch(input.TargetBranch);
	}
}
