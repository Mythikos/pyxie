namespace Pyxie.Runtime.Sandboxes;

/// <summary>Paths as commands in a sandbox see them, which always use forward slashes.</summary>
public static class SandboxPaths
{
	/// <summary>Joins a sandbox folder and a relative path.</summary>
	/// <param name="folder">The folder, as the sandbox sees it.</param>
	/// <param name="relative">The path under it, with forward slashes.</param>
	/// <returns>The joined path.</returns>
	public static string Combine(string folder, string relative)
	{
		return $"{folder.TrimEnd('/')}/{relative.TrimStart('/')}";
	}
}
