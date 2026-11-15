namespace Pyxie.Runtime.Git;

/// <summary>A commit with its full message and what it changed.</summary>
/// <param name="Commit">The commit.</param>
/// <param name="Message">The full message.</param>
/// <param name="Files">The files changed compared with the first parent (or everything, for a root commit).</param>
/// <param name="Patch">The unified diff, shortened when it is very long.</param>
public sealed record GitCommitDetail(GitCommit Commit, string Message, IReadOnlyList<GitChangedFile> Files, string Patch);
