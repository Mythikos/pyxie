namespace Pyxie.Runtime.Git;

/// <summary>A file's content at some revision.</summary>
/// <param name="Path">The path from the repository root.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="IsBinary">Whether the file is binary; its text is then not read.</param>
/// <param name="IsTooLarge">Whether the file is larger than the most Pyxie shows; its text is then not read.</param>
/// <param name="Text">The text, or <see langword="null"/> for binary and over-large files.</param>
public sealed record GitFileContent(string Path, long Size, bool IsBinary, bool IsTooLarge, string? Text);
