namespace Pyxie.Runtime.Sandboxes;

/// <summary>A volume a sandbox keeps its files in, so they outlive the sandbox itself.</summary>
/// <param name="Name">The volume's name; it is created when it does not exist.</param>
/// <param name="PathInside">Where the sandbox sees it.</param>
public sealed record SandboxVolume(string Name, string PathInside);
