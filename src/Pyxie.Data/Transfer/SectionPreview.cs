namespace Pyxie.Data.Transfer;

/// <summary>What importing one section of a settings file into the open project would do, and anything that stops it, as a person reviews it before importing.</summary>
/// <param name="Changes">What the import would change; nothing for a section whose file breaks a rule.</param>
/// <param name="Problems">Why the section cannot be imported: a value the rules refuse, or a project item that cards still need, each in the words the editors use; empty when it can be.</param>
/// <param name="Revision">Identifies the project settings this section was compared with.</param>
public sealed record SectionPreview(SectionChanges Changes, IReadOnlyList<string> Problems, string Revision)
{
	/// <summary>Gets the section.</summary>
	public TransferSection Section => Changes.Section;

	/// <summary>Gets a value indicating whether the section can be imported.</summary>
	public bool CanImport => Problems.Count == 0;
}
