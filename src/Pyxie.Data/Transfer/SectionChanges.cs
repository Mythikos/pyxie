namespace Pyxie.Data.Transfer;

/// <summary>What importing one section of a settings file into a project changes. An import acts only while the project would still change exactly this way: the changes a person reviewed are the changes made.</summary>
/// <param name="Section">The section.</param>
/// <param name="Added">The names of the items the file adds, as the file writes them.</param>
/// <param name="Changed">The names of the project's items whose fields the file changes, as the file writes them; for the sandbox section, <c>Dockerfile</c> when the file's differs.</param>
/// <param name="Removed">The names of the project's items the file does not hold, which the import removes, as stored: agents and categories in the order they were made, gates in the order they run.</param>
/// <param name="Reordered">Whether the gates the project keeps change their order.</param>
public sealed record SectionChanges(TransferSection Section, IReadOnlyList<string> Added, IReadOnlyList<string> Changed, IReadOnlyList<string> Removed, bool Reordered)
{
	/// <summary>Gets a value indicating whether importing the section changes anything.</summary>
	public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0 || Reordered;

	/// <summary>Gets the changes of a section that cannot be planned, because the file breaks a rule there: none.</summary>
	/// <param name="section">The section.</param>
	/// <returns>Changes with nothing in them.</returns>
	public static SectionChanges None(TransferSection section)
	{
		return new SectionChanges(section, [], [], [], false);
	}

	/// <summary>Determines whether two sets of changes change a project the same way, comparing names exactly.</summary>
	/// <param name="other">The other changes.</param>
	/// <returns><see langword="true"/> when the section, every list, and the order flag are the same.</returns>
	public bool SameAs(SectionChanges other)
	{
		return Section == other.Section && Added.SequenceEqual(other.Added, StringComparer.Ordinal) && Changed.SequenceEqual(other.Changed, StringComparer.Ordinal) && Removed.SequenceEqual(other.Removed, StringComparer.Ordinal) && Reordered == other.Reordered;
	}
}
