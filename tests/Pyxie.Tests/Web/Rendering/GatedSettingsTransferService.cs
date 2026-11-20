using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The settings transfer service with its previews and imports counted and holdable by a test; every operation does what the real service does.</summary>
internal sealed class GatedSettingsTransferService : ISettingsTransferService
{
	private readonly ISettingsTransferService _inner;

	/// <summary>Initializes a new instance of the <see cref="GatedSettingsTransferService"/> class.</summary>
	/// <param name="inner">The service that does the work.</param>
	public GatedSettingsTransferService(ISettingsTransferService inner)
	{
		_inner = inner;
	}

	/// <summary>Gets the probe that counts and holds the previews of settings files.</summary>
	public CallProbe Previews { get; } = new();

	/// <summary>Gets the probe that counts and holds the imports of settings files.</summary>
	public CallProbe Imports { get; } = new();

	/// <inheritdoc/>
	public Task<ServiceResult<ProjectSettings>> ExportAsync(int projectId, CancellationToken cancellationToken)
	{
		return _inner.ExportAsync(projectId, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<SettingsImportPreview>> PreviewAsync(int projectId, ProjectSettings settings, CancellationToken cancellationToken)
	{
		await Previews.EnterAsync(cancellationToken);

		return await _inner.PreviewAsync(projectId, settings, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> ImportAsync(ProjectSettings settings, SettingsImportPreview preview, IReadOnlySet<TransferSection> sections, CancellationToken cancellationToken)
	{
		await Imports.EnterAsync(cancellationToken);

		return await _inner.ImportAsync(settings, preview, sections, cancellationToken);
	}
}
