using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A normal clone, with a working tree, of a <see cref="TempGitRepository"/> or of Pyxie's hub, in a folder of its own: it stands in for a person's working copy and its remote. Disposing it deletes the clone.</summary>
public sealed class ClonedRepository : IAsyncDisposable
{
	private readonly TempGitRepository _origin;
	private readonly ScratchFolder _scratch;

	private ClonedRepository(TempGitRepository origin, ScratchFolder scratch, string folder)
	{
		_origin = origin;
		_scratch = scratch;
		Folder = folder;
	}

	/// <summary>Gets the full path of the clone's working tree.</summary>
	public string Folder { get; }

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Clones a repository with git itself, as a person does, and configures a local commit identity, so a global git setting cannot change what a test commits.</summary>
	/// <param name="origin">The repository to clone, bare or not; it becomes the clone's <c>origin</c>.</param>
	/// <returns>The clone, on the branch the origin had checked out.</returns>
	public static async Task<ClonedRepository> CreateAsync(TempGitRepository origin)
	{
		ScratchFolder scratch = ScratchFolder.Create("pyxie-clone");

		try
		{
			string folder = scratch.Combine("clone");
			await origin.RunGitAsync("clone", "--quiet", "--no-hardlinks", "--", origin.Folder, folder);

			ClonedRepository clone = new(origin, scratch, folder);
			await clone.RunGitAsync("config", "user.name", TempGitRepository.AuthorName);
			await clone.RunGitAsync("config", "user.email", "tests@pyxie.invalid");
			await clone.RunGitAsync("config", "commit.gpgsign", "false");
			await clone.RunGitAsync("config", "core.autocrlf", "false");

			return clone;
		}
		catch
		{
			await scratch.DisposeAsync();

			throw;
		}
	}

	/// <summary>Runs any git command in the clone and fails the test when it does not succeed.</summary>
	/// <param name="arguments">The git arguments, each one argument.</param>
	/// <returns>What git printed, trimmed.</returns>
	public async Task<string> RunGitAsync(params string[] arguments)
	{
		return await _origin.RunGitAsync(["-C", Folder, .. arguments]);
	}

	/// <summary>Writes one file and commits everything changed in the clone, on its checked-out branch.</summary>
	/// <param name="relativePath">The path from the clone's root.</param>
	/// <param name="content">The file's text.</param>
	/// <returns>The new commit's hash.</returns>
	public async Task<string> CommitFileAsync(string relativePath, string content)
	{
		string path = Path.GetFullPath(relativePath, Folder);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Folder);
		await File.WriteAllTextAsync(path, content, Token);
		await RunGitAsync("add", "--all");
		ProcessResult staged = await _origin.TryRunGitAsync("-C", Folder, "diff", "--cached", "--quiet");
		Assert.False(staged.Succeeded, $"Nothing to commit for {relativePath}.");
		await RunGitAsync("commit", "--quiet", "--message", $"Add {relativePath}");

		return await RunGitAsync("rev-parse", "HEAD");
	}

	/// <summary>Pushes a branch to the origin the way a person does, and returns the outcome without judging it, for pushes a test expects to be refused.</summary>
	/// <param name="arguments">The arguments after <c>git push origin</c>, such as <c>main</c> or <c>--force</c> and a branch.</param>
	/// <returns>How the run ended, with git's combined output.</returns>
	public async Task<ProcessResult> TryPushAsync(params string[] arguments)
	{
		return await _origin.TryRunGitAsync(["-C", Folder, "push", "--quiet", "origin", .. arguments]);
	}

	/// <summary>Pushes a branch to the origin and fails the test when the push is refused.</summary>
	/// <param name="arguments">The arguments after <c>git push origin</c>, such as <c>main</c>.</param>
	/// <returns>A task that completes when the push is done.</returns>
	public async Task PushAsync(params string[] arguments)
	{
		await RunGitAsync(["push", "--quiet", "origin", .. arguments]);
	}

	/// <summary>Fetches the origin's branches into the clone's remote-tracking branches, which moves what the clone is ahead of or behind.</summary>
	/// <returns>A task that completes when the fetch is done.</returns>
	public async Task FetchAsync()
	{
		await RunGitAsync("fetch", "--quiet", "origin");
	}

	/// <summary>Deletes the clone.</summary>
	/// <returns>A task that completes when the clone is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		await _scratch.DisposeAsync();
	}
}
