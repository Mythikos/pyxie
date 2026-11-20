namespace Pyxie.Runtime.Sandboxes;

/// <summary>A credential source checked on the host.</summary>
/// <param name="Path">The file the person configures.</param>
/// <param name="Found">Whether it contains the expected credential.</param>
public sealed record SandboxCredentialStatus(string Path, bool Found);
