namespace Pyxie.Data.Transfer.Planning;

/// <summary>How the items of one section of a settings file meet a project's items of that section, matched by name.</summary>
/// <typeparam name="TExisting">The kind of item the project stores.</typeparam>
/// <typeparam name="TIncoming">The kind of item the file holds.</typeparam>
/// <param name="Matched">Each project item the file holds, with the file's item, in the file's order.</param>
/// <param name="Added">The file's items the project does not have, in the file's order.</param>
/// <param name="Removed">The project's items the file does not hold, in the project's order: agents and categories as they were made, gates as they run.</param>
internal sealed record NamePlan<TExisting, TIncoming>(IReadOnlyList<(TExisting Existing, TIncoming Incoming)> Matched, IReadOnlyList<TIncoming> Added, IReadOnlyList<TExisting> Removed);
