using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Reads and saves the open project's sandbox settings (see <c>docs/sandboxing.md</c>).</summary>
public interface ISandboxSettingsService
{
	/// <summary>Gets the project's sandbox settings, or the defaults when they were never saved.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The settings; <see cref="SandboxSettings.Version"/> is 0 for the defaults.</returns>
	Task<SandboxSettings> GetAsync(CancellationToken cancellationToken);

	/// <summary>Saves the project's sandbox settings, only while they are still the version the person read.</summary>
	/// <param name="input">The settings as entered.</param>
	/// <param name="expectedVersion">The version the person read; 0 for the defaults.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The saved settings, or why they were not saved.</returns>
	Task<ServiceResult<SandboxSettings>> SaveAsync(SandboxSettingsInput input, int expectedVersion, CancellationToken cancellationToken);
}
