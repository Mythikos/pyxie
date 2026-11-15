namespace Pyxie.Runtime.Repositories;

/// <summary>A branch's files as a zip file, ready to download.</summary>
/// <param name="FileName">The name to save the zip under.</param>
/// <param name="Content">The zip. Closing it deletes the temporary file behind it.</param>
public sealed record RepositoryArchive(string FileName, Stream Content);
