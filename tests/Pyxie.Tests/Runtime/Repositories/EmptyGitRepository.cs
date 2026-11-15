using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>A freshly initialized git repository with no commits at all, for the cases a repository cannot be used yet. Disposing it deletes the folder.</summary>
public sealed class EmptyGitRepository : IAsyncDisposable
{
	private EmptyGitRepository(string folder)
	{
		Folder = folder;
	}

	/// <summary>Gets the full path of the repository's working tree.</summary>
	public string Folder { get; }

	/// <summary>Runs <c>git init -b main</c> in a new temporary folder.</summary>
	/// <returns>The repository, whose <c>main</c> branch has no commits.</returns>
	public static async Task<EmptyGitRepository> CreateAsync()
	{
		EmptyGitRepository repository = new(TempFolder.Create("pyxie-empty"));

		try
		{
			ProcessRequest request = new("git", ["init", "--quiet", "-b", "main"], repository.Folder) { Timeout = TimeSpan.FromMinutes(2) };
			using ProcessRunner runner = new(TimeProvider.System);
			ProcessResult result = await runner.RunAsync(request, TestContext.Current.CancellationToken);
			Assert.True(result.Succeeded, $"git init failed ({result.Outcome}, exit code {result.ExitCode}): {result.StartError ?? result.Output}");

			return repository;
		}
		catch
		{
			await repository.DisposeAsync();

			throw;
		}
	}

	/// <summary>Deletes the repository's folder.</summary>
	/// <returns>A task that completes when the folder is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		await TempFolder.DeleteAsync(Folder);
	}
}
