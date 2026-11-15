namespace Pyxie.Runtime.Git;

/// <summary>A local branch.</summary>
/// <param name="Name">The branch name, without <c>refs/heads/</c>.</param>
/// <param name="Sha">The commit it points at.</param>
/// <param name="CommittedUtc">When that commit was made, in UTC.</param>
/// <param name="Subject">That commit's subject line.</param>
public sealed record GitBranch(string Name, string Sha, DateTime CommittedUtc, string Subject);
