using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>
/// Reads and saves the open project's sandbox settings. A save lands only while the settings are still the version the person read, so two tabs never overwrite each other.
/// </summary>
public sealed class SandboxSettingsService : ISandboxSettingsService
{
	private const string ChangedMessage = "The sandbox settings were changed elsewhere. Reload them, then save again.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="SandboxSettingsService"/> class.</summary>
	/// <param name="contextFactory">The open project's database.</param>
	/// <param name="timeProvider">The clock that stamps saves.</param>
	public SandboxSettingsService(IDbContextFactory<ProjectDbContext> contextFactory, TimeProvider timeProvider)
	{
		_contextFactory = contextFactory;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<SandboxSettings> GetAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await SandboxSettingsStore.FindAsync(context, cancellationToken) ?? new SandboxSettings { Dockerfile = SandboxDefaults.Dockerfile };
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<SandboxSettings>> SaveAsync(SandboxSettingsInput input, int expectedVersion, CancellationToken cancellationToken)
	{
		if (SandboxSettingsInputRules.ValidateDockerfile(input.Dockerfile) is string dockerfileError)
		{
			return ServiceResult<SandboxSettings>.Failure(dockerfileError);
		}

		string dockerfile = SandboxSettingsInputRules.NormalizeDockerfile(input.Dockerfile);
		DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		SandboxSettings? stored = await SandboxSettingsStore.FindAsync(context, cancellationToken);

		if (stored is null)
		{
			return expectedVersion == 0 ? await InsertAsync(context, dockerfile, now, cancellationToken) : ServiceResult<SandboxSettings>.Failure(ChangedMessage);
		}

		int updated = await SandboxSettingsStore.UpdateAsync(context, stored.Id, expectedVersion, dockerfile, now, cancellationToken);

		return updated == 0 ? ServiceResult<SandboxSettings>.Failure(ChangedMessage) : ServiceResult<SandboxSettings>.Success(new SandboxSettings { Id = stored.Id, Dockerfile = dockerfile, UpdatedUtc = now, Version = expectedVersion + 1 });
	}

	// The first save inserts the row; the count after it, in the same transaction, refuses a second first save made at the same time.
	private static async Task<ServiceResult<SandboxSettings>> InsertAsync(ProjectDbContext context, string dockerfile, DateTime now, CancellationToken cancellationToken)
	{
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		if (await SandboxSettingsStore.InsertFirstAsync(context, dockerfile, now, cancellationToken) is not SandboxSettings settings)
		{
			return ServiceResult<SandboxSettings>.Failure(ChangedMessage);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<SandboxSettings>.Success(settings);
	}
}
