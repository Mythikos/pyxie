using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A bare repository in a folder of its own, for tests of what the runtime does in Pyxie's hub, which is bare: it has no working tree, so git's files are in the folder itself. <see cref="Repository"/> gives it the helpers of <see cref="TempGitRepository"/> for reading and branching. Disposing it deletes the folder.</summary>
public sealed class BareRepository : IAsyncDisposable
{
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	private readonly ScratchFolder _scratch;

	private BareRepository(ScratchFolder scratch, TempGitRepository repository)
	{
		_scratch = scratch;
		Repository = repository;
	}

	/// <summary>Gets the repository, attached with <see cref="TempGitRepository.AttachAsync"/>: its helpers run git in this folder, and disposing it alone leaves the folder in place.</summary>
	public TempGitRepository Repository { get; }

	/// <summary>Gets the full path of the repository, which holds git's files itself.</summary>
	public string Folder => Repository.Folder;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Clones a repository the way Pyxie clones the one it imports into a hub (<see cref="Pyxie.Runtime.Git.GitClient.CloneBareAsync"/>): every branch of the origin is a branch of the clone, and HEAD names the branch the origin has checked out. The clone shares no files with the origin, so commits made in the origin afterwards are not in it.</summary>
	/// <param name="origin">The repository to clone.</param>
	/// <returns>The clone. The test fails when git does not succeed.</returns>
	public static async Task<BareRepository> CloneAsync(TempGitRepository origin)
	{
		ScratchFolder scratch = ScratchFolder.Create("pyxie-bare");

		try
		{
			string folder = scratch.Combine("hub");
			ProcessResult result = await origin.Git.CloneBareAsync(origin.Folder, folder, Token);
			Assert.True(result.Succeeded, $"The bare clone of {origin.Folder} failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

			return new BareRepository(scratch, await TempGitRepository.AttachAsync(folder));
		}
		catch
		{
			await scratch.DisposeAsync();

			throw;
		}
	}

	/// <summary>Creates a bare repository with no commits and no branches: a receiver that holds nothing a bundle could build on.</summary>
	/// <returns>The repository. The test fails when git does not succeed.</returns>
	public static async Task<BareRepository> CreateEmptyAsync()
	{
		ScratchFolder scratch = ScratchFolder.Create("pyxie-bare");

		try
		{
			string folder = scratch.Combine("empty");
			using ProcessRunner runner = new(TimeProvider.System);
			ProcessRequest request = new("git", ["init", "--quiet", "--bare", "--initial-branch", TempGitRepository.MainBranch, folder], scratch.Folder) { Timeout = s_commandTimeout };
			ProcessResult result = await runner.RunAsync(request, Token);
			Assert.True(result.Succeeded, $"git init --bare failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

			return new BareRepository(scratch, await TempGitRepository.AttachAsync(folder));
		}
		catch
		{
			await scratch.DisposeAsync();

			throw;
		}
	}

	/// <summary>Lists every ref in the repository with the object it points at, so a test can compare the refs before and after a command.</summary>
	/// <returns>One <c>refname object</c> line per ref, ordered by name the way git orders them (ordinal); empty when the repository has no refs.</returns>
	public async Task<IReadOnlyList<string>> ListRefsAsync()
	{
		string output = await Repository.RunGitAsync("for-each-ref", "--format=%(refname) %(objectname)");

		return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	/// <summary>Deletes the repository's folder.</summary>
	/// <returns>A task that completes when the folder is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		await Repository.DisposeAsync();
		await _scratch.DisposeAsync();
	}
}
