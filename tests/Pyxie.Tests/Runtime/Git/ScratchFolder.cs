using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A temporary folder a test owns, for clones, worktrees, and anything else git writes outside the repository under test. Disposing it deletes it, read-only git files included.</summary>
public sealed class ScratchFolder : IAsyncDisposable
{
	private ScratchFolder(string folder)
	{
		Folder = folder;
	}

	/// <summary>Gets the full path of the folder, which exists.</summary>
	public string Folder { get; }

	/// <summary>Creates the folder.</summary>
	/// <param name="prefix">The start of the folder's name.</param>
	/// <returns>The folder.</returns>
	public static ScratchFolder Create(string prefix = "pyxie-scratch")
	{
		return new ScratchFolder(TempFolder.Create(prefix));
	}

	/// <summary>Gets a path inside the folder without creating it.</summary>
	/// <param name="parts">The folder and file names below this folder.</param>
	/// <returns>The full path.</returns>
	public string Combine(params string[] parts)
	{
		return Path.Combine([Folder, .. parts]);
	}

	/// <summary>Deletes the folder.</summary>
	/// <returns>A task that completes when the folder is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		await TempFolder.DeleteAsync(Folder);
	}
}
