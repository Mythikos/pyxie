using Pyxie.Data.Enums;

namespace Pyxie.Runtime.Repositories;

/// <summary>An import that is running or that failed, as <see cref="RepositoryImports"/> reports it.</summary>
/// <param name="Source">Whether a folder or a URL is being imported.</param>
/// <param name="Origin">The folder or URL, as entered.</param>
/// <param name="Error">Why the import failed, or <see langword="null"/> while it runs.</param>
public sealed record RepositoryImport(RepositorySource Source, string Origin, string? Error)
{
	/// <summary>Gets a value indicating whether the import is still running.</summary>
	public bool IsRunning => Error is null;
}
