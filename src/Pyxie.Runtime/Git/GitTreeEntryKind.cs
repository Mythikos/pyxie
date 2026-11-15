namespace Pyxie.Runtime.Git;

/// <summary>What an entry in a directory listing is.</summary>
public enum GitTreeEntryKind
{
	/// <summary>A directory.</summary>
	Directory = 0,

	/// <summary>A file.</summary>
	File = 1,

	/// <summary>A submodule: a commit of another repository.</summary>
	Submodule = 2,
}
