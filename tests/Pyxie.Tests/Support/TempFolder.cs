using Pyxie.Runtime.Workspaces;

namespace Pyxie.Tests.Support;

/// <summary>Creates and removes the private temporary folders that runtime tests work in, so every test has folders of its own and leaves none behind.</summary>
public static class TempFolder
{
	// Windows can hold a folder open for a moment after the last process that used it was killed (the harness, git, antivirus), so removal is retried briefly.
	private const int MaxDeleteAttempts = 10;
	private static readonly TimeSpan s_deleteRetryDelay = TimeSpan.FromMilliseconds(200);

	/// <summary>Creates a new, empty folder under the system temporary folder. The name is short on purpose: git worktrees and nested paths hit Windows path limits quickly.</summary>
	/// <param name="prefix">What the folder is for, which starts its name so a leftover folder can be traced to the helper that made it.</param>
	/// <returns>The full path of the new folder.</returns>
	public static string Create(string prefix)
	{
		string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid().ToString("N")[..12]}");
		Directory.CreateDirectory(path);

		return path;
	}

	/// <summary>Deletes a folder and everything in it, including the read-only files git writes. Does nothing when the folder is already gone.</summary>
	/// <param name="path">The folder.</param>
	/// <returns>A task that completes when the folder is gone.</returns>
	/// <exception cref="IOException">The folder still could not be deleted after several attempts; a process may still be using it.</exception>
	public static async Task DeleteAsync(string path)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				Folders.Delete(path);

				return;
			}
			catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < MaxDeleteAttempts)
			{
				await Task.Delay(s_deleteRetryDelay);
			}
		}
	}
}
