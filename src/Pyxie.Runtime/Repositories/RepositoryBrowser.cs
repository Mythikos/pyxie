using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Runtime.Git;

namespace Pyxie.Runtime.Repositories;

/// <summary>Reads the open project's repository for the Repository page: its branches, files at any revision, history, and commits. Nothing here changes the repository.</summary>
public sealed class RepositoryBrowser
{
	/// <summary>The number of commits on one page of history.</summary>
	public const int CommitsPerPage = 50;

	/// <summary>The largest file whose text is shown, in bytes.</summary>
	public const int MaxFileBytes = 512 * 1024;

	// A diff longer than this is cut, keeping its beginning and end.
	private const int MaxPatchLength = 400_000;

	private readonly OpenProject _openProject;
	private readonly IRepositoryService _repositories;
	private readonly GitClient _git;

	/// <summary>Initializes a new instance of the <see cref="RepositoryBrowser"/> class.</summary>
	/// <param name="openProject">The open project; there is nothing to read while none is open.</param>
	/// <param name="repositories">The repository service.</param>
	/// <param name="git">The git client.</param>
	public RepositoryBrowser(OpenProject openProject, IRepositoryService repositories, GitClient git)
	{
		_openProject = openProject;
		_repositories = repositories;
		_git = git;
	}

	/// <summary>Gets the open project's repository with its branches and how it stands against its remote.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The overview, or <see langword="null"/> when no project is open or its repository is not connected yet.</returns>
	/// <exception cref="GitException">git could not read the repository, for example because its folder was moved.</exception>
	public async Task<RepositoryOverview?> GetOverviewAsync(CancellationToken cancellationToken)
	{
		if (await GetRepositoryAsync(cancellationToken) is not Repository repository)
		{
			return null;
		}

		IReadOnlyList<GitBranch> branches = await _git.ListBranchesAsync(repository.Path, cancellationToken);

		return new RepositoryOverview(repository, SourceNames.NameOf(repository.Origin), branches);
	}

	/// <summary>Lists a directory at a revision.</summary>
	/// <param name="repository">The connected repository.</param>
	/// <param name="revision">A branch or commit.</param>
	/// <param name="path">The directory, with forward slashes; empty for the root.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The entries, or <see langword="null"/> when the path is not a directory at that revision.</returns>
	public Task<IReadOnlyList<GitTreeEntry>?> ListAsync(Repository repository, string revision, string path, CancellationToken cancellationToken)
	{
		return _git.ListTreeAsync(repository.Path, revision, path, cancellationToken);
	}

	/// <summary>Reads a file at a revision.</summary>
	/// <param name="repository">The connected repository.</param>
	/// <param name="revision">A branch or commit.</param>
	/// <param name="path">The file, with forward slashes.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The content, or <see langword="null"/> when the path is not a file at that revision. Text is read only up to <see cref="MaxFileBytes"/>.</returns>
	public Task<GitFileContent?> ReadAsync(Repository repository, string revision, string path, CancellationToken cancellationToken)
	{
		return _git.ReadFileAsync(repository.Path, revision, path, MaxFileBytes, cancellationToken);
	}

	/// <summary>Gets one page of history from a revision, newest first.</summary>
	/// <param name="repository">The connected repository.</param>
	/// <param name="revision">A branch or commit.</param>
	/// <param name="page">The zero-based page.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>Up to <see cref="CommitsPerPage"/> commits.</returns>
	public Task<IReadOnlyList<GitCommit>> GetHistoryAsync(Repository repository, string revision, int page, CancellationToken cancellationToken)
	{
		return _git.LogAsync(repository.Path, revision, Math.Max(0, page) * CommitsPerPage, CommitsPerPage, cancellationToken);
	}

	/// <summary>Writes the files of a branch to a zip file inside a folder named after the repository, for a person to download. The zip lives in the system's temporary folder and is deleted when the returned stream is closed.</summary>
	/// <param name="branch">The branch.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The zip, or <see langword="null"/> when no project is open, its repository is not connected yet, or it has no such branch.</returns>
	public async Task<RepositoryArchive?> ArchiveAsync(string branch, CancellationToken cancellationToken)
	{
		if (await GetRepositoryAsync(cancellationToken) is not Repository repository
			|| !await _git.BranchExistsAsync(repository.Path, branch, cancellationToken))
		{
			return null;
		}

		string name = SourceNames.FileNameOf(SourceNames.NameOf(repository.Origin));
		string zipPath = Path.Combine(Path.GetTempPath(), $"pyxie-{Guid.NewGuid():N}.zip");
		try
		{
			await _git.ArchiveAsync(repository.Path, $"refs/heads/{branch}", name, zipPath, cancellationToken);

			FileStream zip = new(zipPath, FileMode.Open, FileAccess.Read, FileShare.Delete, bufferSize: 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

			return new RepositoryArchive($"{name}-{SourceNames.FileNameOf(branch)}.zip", zip);
		}
		catch
		{
			File.Delete(zipPath);
			throw;
		}
	}

	/// <summary>Gets a commit with what it changed.</summary>
	/// <param name="repository">The connected repository.</param>
	/// <param name="sha">The commit hash.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The commit, or <see langword="null"/> when it does not exist.</returns>
	public Task<GitCommitDetail?> GetCommitAsync(Repository repository, string sha, CancellationToken cancellationToken)
	{
		return _git.GetCommitAsync(repository.Path, sha, MaxPatchLength, cancellationToken);
	}

	// The project database exists only while a project is open, so it is not asked before.
	private async Task<Repository?> GetRepositoryAsync(CancellationToken cancellationToken)
	{
		return _openProject.Current is null ? null : await _repositories.GetAsync(cancellationToken);
	}
}
