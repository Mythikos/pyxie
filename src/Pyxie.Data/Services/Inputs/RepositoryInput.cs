using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The fields that connect a repository. The runtime clones the source into the project's hub and checks it before it connects it.</summary>
/// <param name="Source">Where the repository was imported from.</param>
/// <param name="Path">The absolute path of the hub.</param>
/// <param name="Origin">The folder or URL the clone was made from.</param>
/// <param name="TargetBranch">The branch finished cards are merged into.</param>
public sealed record RepositoryInput(RepositorySource Source, string Path, string Origin, string TargetBranch);
