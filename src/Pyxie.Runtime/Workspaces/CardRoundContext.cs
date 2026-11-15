using Pyxie.Data.Entities;

namespace Pyxie.Runtime.Workspaces;

/// <summary>The open project a round runs for.</summary>
/// <param name="ProjectFolder">The project's folder.</param>
/// <param name="ProjectKey">The project's key, which names its sandboxes (see <c>SandboxNames.ProjectKey</c>).</param>
/// <param name="Repository">The project's repository: the hub and its target branch.</param>
/// <param name="Dockerfile">The Dockerfile of the project's sandbox image.</param>
public sealed record CardRoundContext(string ProjectFolder, string ProjectKey, Repository Repository, string Dockerfile);
