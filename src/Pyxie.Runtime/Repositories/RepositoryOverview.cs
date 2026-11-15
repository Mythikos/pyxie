using Pyxie.Data.Entities;
using Pyxie.Runtime.Git;

namespace Pyxie.Runtime.Repositories;

/// <summary>What the Repository page shows above its files and history.</summary>
/// <param name="Repository">The connected repository.</param>
/// <param name="Name">The last part of the folder or URL the repository was imported from.</param>
/// <param name="Branches">The local branches, most recently committed first.</param>
public sealed record RepositoryOverview(Repository Repository, string Name, IReadOnlyList<GitBranch> Branches);
