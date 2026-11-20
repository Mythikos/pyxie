using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;

namespace Pyxie.Data.Services;

/// <summary>Exports a project's settings (its agents, gates, categories, and sandbox Dockerfile) and imports them into a project, section by section. Each imported section replaces the project's: items match by name, ignoring case, and are updated in place so cards keep referring to them; the file's other items are added and the project's other items removed. An import is all or nothing under the rules the editors apply, and only while the project would still change the way its preview showed.</summary>
public interface ISettingsTransferService
{
	/// <summary>Reads a project's settings, every section, as a settings file holds them.</summary>
	/// <param name="projectId">The project, which must be the open one.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The settings, or a failure when the project is not open.</returns>
	Task<ServiceResult<ProjectSettings>> ExportAsync(int projectId, CancellationToken cancellationToken);

	/// <summary>Works out what importing each section of a settings file into a project would change, and what stops it, without changing anything.</summary>
	/// <param name="projectId">The project, which must be the open one.</param>
	/// <param name="settings">The file's settings.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>One preview for each section the file holds, or a failure when the project is not open.</returns>
	Task<ServiceResult<SettingsImportPreview>> PreviewAsync(int projectId, ProjectSettings settings, CancellationToken cancellationToken);

	/// <summary>Imports sections of a settings file into the project a preview was made for, in one transaction: either every picked section is imported or nothing changes.</summary>
	/// <param name="settings">The file's settings.</param>
	/// <param name="preview">The preview the person reviewed.</param>
	/// <param name="sections">The sections to import.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure, with nothing changed, when no section is picked, a picked section breaks a rule or is needed by cards, another project is open, or the project changed since the preview; it lists every reason, one per line.</returns>
	Task<ServiceResult> ImportAsync(ProjectSettings settings, SettingsImportPreview preview, IReadOnlySet<TransferSection> sections, CancellationToken cancellationToken);
}
