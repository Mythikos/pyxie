namespace Pyxie.Runtime.Git;

/// <summary>A file a commit changed, compared with its first parent.</summary>
/// <param name="Path">The path after the change.</param>
/// <param name="OldPath">The path before a rename or copy; otherwise <see langword="null"/>.</param>
/// <param name="Additions">Lines added, or <see langword="null"/> for a binary file.</param>
/// <param name="Deletions">Lines removed, or <see langword="null"/> for a binary file.</param>
public sealed record GitChangedFile(string Path, string? OldPath, int? Additions, int? Deletions);
