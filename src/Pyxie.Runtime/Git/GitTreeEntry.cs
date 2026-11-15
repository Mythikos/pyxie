namespace Pyxie.Runtime.Git;

/// <summary>An entry of a directory at some revision.</summary>
/// <param name="Name">The entry's own name.</param>
/// <param name="Path">The path from the repository root, with forward slashes.</param>
/// <param name="Kind">Whether it is a directory, a file, or a submodule.</param>
/// <param name="Size">The size in bytes, for files; otherwise <see langword="null"/>.</param>
public sealed record GitTreeEntry(string Name, string Path, GitTreeEntryKind Kind, long? Size);
