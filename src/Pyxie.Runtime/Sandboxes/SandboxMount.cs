namespace Pyxie.Runtime.Sandboxes;

/// <summary>A folder or file on the host that a sandbox sees.</summary>
/// <param name="HostPath">The full path on the host.</param>
/// <param name="PathInside">Where the sandbox sees it.</param>
/// <param name="ReadOnly">Whether the sandbox can only read it.</param>
public sealed record SandboxMount(string HostPath, string PathInside, bool ReadOnly);
