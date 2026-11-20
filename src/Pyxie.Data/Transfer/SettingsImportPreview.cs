namespace Pyxie.Data.Transfer;

/// <summary>What importing a settings file into a project would do, section by section, as a person reviews it before importing. An import refuses to act on a preview of another project, or on one whose changes (additions, changes, removals, or gate order) no longer match the project.</summary>
/// <param name="ProjectId">The project the preview was made for.</param>
/// <param name="Sections">One preview for each section the file holds, in the order of <see cref="ProjectSettings.Sections"/>.</param>
public sealed record SettingsImportPreview(int ProjectId, IReadOnlyList<SectionPreview> Sections)
{
	/// <summary>Finds the preview of a section.</summary>
	/// <param name="section">The section.</param>
	/// <returns>The section's preview, or <see langword="null"/> when the file does not hold it.</returns>
	public SectionPreview? Find(TransferSection section)
	{
		return Sections.FirstOrDefault(preview => preview.Section == section);
	}
}
