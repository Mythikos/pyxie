namespace Pyxie.Runtime.Git;

/// <summary>Whether a git command only reads the repository or changes it, which decides whether a caller's cancellation may stop it.</summary>
internal enum GitAccess
{
	/// <summary>The command only reads; cancelling stops it at once.</summary>
	Read = 0,

	/// <summary>The command changes the repository or a working tree. Once started it runs to the end, bounded by its own timeout, because git killed part-way leaves lock files and half-written state behind.</summary>
	Change = 1,
}
