namespace Pyxie.Runtime.Sandboxes;

/// <summary>A file on the host whose text commands in the sandbox get as an environment variable. The file is seen read-only and its text never appears on a command line, so it is not in Pyxie's process list or logs.</summary>
/// <param name="HostPath">The full path of the file on the host.</param>
/// <param name="EnvironmentVariable">The variable commands in the sandbox get its text as.</param>
public sealed record SandboxSecret(string HostPath, string EnvironmentVariable);
