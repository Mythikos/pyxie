namespace Pyxie.Runtime.Git;

/// <summary>The outcome of merging two commits without a working tree (see <see cref="GitClient.MergeTreeAsync"/>).</summary>
/// <param name="Tree">The merged tree. With conflicts it holds conflict markers and must not be committed.</param>
/// <param name="ConflictedFiles">The files that conflict; empty for a clean merge.</param>
public sealed record GitMergeTree(string Tree, IReadOnlyList<string> ConflictedFiles)
{
	/// <summary>Gets a value indicating whether the merge is clean.</summary>
	public bool IsClean => ConflictedFiles.Count == 0;
}
