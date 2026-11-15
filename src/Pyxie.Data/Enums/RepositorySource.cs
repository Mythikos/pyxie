namespace Pyxie.Data.Enums;

/// <summary>Where the connected repository was imported from. Either way Pyxie works in its own clone and never changes the source.</summary>
public enum RepositorySource
{
	/// <summary>A local git working copy, cloned into the workspace root.</summary>
	Folder = 0,

	/// <summary>A URL, cloned into the workspace root.</summary>
	Url = 1,
}
