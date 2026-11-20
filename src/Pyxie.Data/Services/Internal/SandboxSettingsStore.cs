using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Data.Services.Internal;

/// <summary>Owns how a project's sandbox settings are read and written within a caller's context, for <see cref="SandboxSettingsService"/> and the settings import alike. A project database holds at most one row; until one is saved, the defaults apply. Every write advances the row's version, which is what refuses a save made from settings read before it.</summary>
internal static class SandboxSettingsStore
{
	/// <summary>Reads the stored settings, untracked.</summary>
	/// <param name="context">The context.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The stored settings, or <see langword="null"/> when they were never saved.</returns>
	public static Task<SandboxSettings?> FindAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		return context.SandboxSettings.AsNoTracking().OrderBy(settings => settings.Id).FirstOrDefaultAsync(cancellationToken);
	}

	/// <summary>Gets the Dockerfile in effect, normalized: the stored one, or the default while none was saved.</summary>
	/// <param name="stored">The stored settings, or <see langword="null"/>.</param>
	/// <returns>The Dockerfile as <see cref="SandboxSettingsInputRules.NormalizeDockerfile"/> writes it.</returns>
	public static string EffectiveDockerfile(SandboxSettings? stored)
	{
		return SandboxSettingsInputRules.NormalizeDockerfile(stored?.Dockerfile ?? SandboxDefaults.Dockerfile);
	}

	/// <summary>Writes a Dockerfile to the stored row, only while the row is still at a version, and advances the version.</summary>
	/// <param name="context">The context.</param>
	/// <param name="id">The stored row.</param>
	/// <param name="expectedVersion">The version the row must still have.</param>
	/// <param name="dockerfile">The Dockerfile, normalized.</param>
	/// <param name="now">The time of the save.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>1 when the row was written; 0 when its version has moved on.</returns>
	public static Task<int> UpdateAsync(ProjectDbContext context, int id, int expectedVersion, string dockerfile, DateTime now, CancellationToken cancellationToken)
	{
		return context.SandboxSettings.Where(settings => settings.Id == id && settings.Version == expectedVersion).ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.Dockerfile, dockerfile).SetProperty(settings => settings.UpdatedUtc, now).SetProperty(settings => settings.Version, settings => settings.Version + 1), cancellationToken);
	}

	/// <summary>Inserts the first row, at version 1, inside the caller's transaction. The count after the insert refuses a second first save made at the same time; the caller then rolls back.</summary>
	/// <param name="context">The context, inside the caller's transaction.</param>
	/// <param name="dockerfile">The Dockerfile, normalized.</param>
	/// <param name="now">The time of the save.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The inserted settings, or <see langword="null"/> when another row exists.</returns>
	public static async Task<SandboxSettings?> InsertFirstAsync(ProjectDbContext context, string dockerfile, DateTime now, CancellationToken cancellationToken)
	{
		SandboxSettings settings = new() { Dockerfile = dockerfile, UpdatedUtc = now, Version = 1 };
		context.SandboxSettings.Add(settings);
		await context.SaveChangesAsync(cancellationToken);

		return await context.SandboxSettings.CountAsync(cancellationToken) > 1 ? null : settings;
	}
}
