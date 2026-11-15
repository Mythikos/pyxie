using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Inputs;
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

		return await context.SandboxSettings.AsNoTracking().OrderBy(settings => settings.Id).FirstOrDefaultAsync(cancellationToken)
			?? new SandboxSettings { Dockerfile = SandboxDefaults.Dockerfile };
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
		SandboxSettings? stored = await context.SandboxSettings.AsNoTracking().OrderBy(settings => settings.Id).FirstOrDefaultAsync(cancellationToken);

		if (stored is null)
		{
			return expectedVersion == 0 ? await InsertAsync(context, dockerfile, now, cancellationToken) : ServiceResult<SandboxSettings>.Failure(ChangedMessage);
		}

		int updated = await context.SandboxSettings.Where(settings => settings.Id == stored.Id && settings.Version == expectedVersion).ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.Dockerfile, dockerfile).SetProperty(settings => settings.UpdatedUtc, now).SetProperty(settings => settings.Version, settings => settings.Version + 1), cancellationToken);

		return updated == 0 ? ServiceResult<SandboxSettings>.Failure(ChangedMessage) : ServiceResult<SandboxSettings>.Success(new SandboxSettings { Id = stored.Id, Dockerfile = dockerfile, UpdatedUtc = now, Version = expectedVersion + 1 });
	}

	// The first save inserts the row; the count after it, in the same transaction, refuses a second first save made at the same time.
	private static async Task<ServiceResult<SandboxSettings>> InsertAsync(ProjectDbContext context, string dockerfile, DateTime now, CancellationToken cancellationToken)
	{
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		SandboxSettings settings = new() { Dockerfile = dockerfile, UpdatedUtc = now, Version = 1 };
		context.SandboxSettings.Add(settings);
		await context.SaveChangesAsync(cancellationToken);

		if (await context.SandboxSettings.CountAsync(cancellationToken) > 1)
		{
			return ServiceResult<SandboxSettings>.Failure(ChangedMessage);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<SandboxSettings>.Success(settings);
	}
}
