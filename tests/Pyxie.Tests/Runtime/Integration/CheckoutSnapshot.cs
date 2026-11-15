using System.Security.Cryptography;

using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

/// <summary>Everything about a working tree a merge could disturb: the bytes of every file outside <c>.git</c> (line endings included), the commit checked out, the index, and what git reports as changed. Capturing it before and after proves a merge into the repository's branches left a person's checkout alone.</summary>
/// <param name="Files">Each file's path from the root with forward slashes and the SHA-256 of its bytes.</param>
/// <param name="Head">The checked-out commit, or the branch name when HEAD is unborn.</param>
/// <param name="Index">The staged entries (<c>git ls-files --stage</c>).</param>
/// <param name="Status">The changes git reports (<c>git status --porcelain</c>).</param>
public sealed record CheckoutSnapshot(IReadOnlyList<string> Files, string Head, string Index, string Status)
{
	/// <summary>Captures a working tree.</summary>
	/// <param name="repository">The repository whose own folder is captured.</param>
	/// <returns>The snapshot.</returns>
	public static async Task<CheckoutSnapshot> CaptureAsync(TempGitRepository repository)
	{
		string gitFolder = Path.Combine(repository.Folder, ".git") + Path.DirectorySeparatorChar;
		List<string> files = [];

		foreach (string path in Directory.EnumerateFiles(repository.Folder, "*", SearchOption.AllDirectories))
		{
			if (path.StartsWith(gitFolder, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
			files.Add($"{Path.GetRelativePath(repository.Folder, path).Replace('\\', '/')} {Convert.ToHexString(SHA256.HashData(bytes))} ({bytes.Length} bytes)");
		}

		files.Sort(StringComparer.Ordinal);

		return new CheckoutSnapshot(files, await repository.GetHashAsync(), await repository.RunGitAsync("ls-files", "--stage"), await repository.RunGitAsync("status", "--porcelain", "--untracked-files=all"));
	}

	/// <summary>Fails the test unless a working tree is exactly as this snapshot captured it.</summary>
	/// <param name="repository">The repository whose own folder is compared.</param>
	/// <returns>A task that completes when the comparison passed.</returns>
	public async Task AssertUnchangedAsync(TempGitRepository repository)
	{
		CheckoutSnapshot after = await CaptureAsync(repository);

		Assert.Equal(Files, after.Files);
		Assert.Equal(Head, after.Head);
		Assert.Equal(Index, after.Index);
		Assert.Equal(Status, after.Status);
	}

	/// <summary>Fails the test unless the files and the index of a working tree are exactly as this snapshot captured them. The checked-out commit and what git reports as changed are left out, for a checkout of the branch a merge moved: its commit moves with the branch, and git then reports the difference between that commit and the files, which Pyxie does not bring up to date.</summary>
	/// <param name="repository">The repository whose own folder is compared.</param>
	/// <returns>A task that completes when the comparison passed.</returns>
	public async Task AssertFilesAndIndexUnchangedAsync(TempGitRepository repository)
	{
		CheckoutSnapshot after = await CaptureAsync(repository);

		Assert.Equal(Files, after.Files);
		Assert.Equal(Index, after.Index);
	}
}
