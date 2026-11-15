namespace Pyxie.Runtime.Workspaces;

/// <summary>File-system helpers for the folders Pyxie owns under the workspace root.</summary>
public static class Folders
{
	/// <summary>Deletes a folder and everything in it, if it exists. Only ever used on folders under the workspace root that git no longer tracks.</summary>
	/// <param name="path">The folder.</param>
	public static void Delete(string path)
	{
		if (!Directory.Exists(path))
		{
			return;
		}

		// git marks object files read-only, which Directory.Delete refuses on Windows.
		foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(path, recursive: true);
	}
}
