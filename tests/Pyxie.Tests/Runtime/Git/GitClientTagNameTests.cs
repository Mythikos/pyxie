using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A tag can share its name with a branch, and git then shortens the branch to <c>heads/&lt;name&gt;</c>. Pyxie reads full refs, so the plain branch name always comes back.</summary>
public sealed class GitClientTagNameTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetCurrentBranchAsync_WhenATagHasTheBranchsName_ReturnsThePlainBranchName()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("tag", "main");

		string? branch = await repository.Git.GetCurrentBranchAsync(repository.Folder, Token);

		Assert.Equal("main", branch);
	}

	[Fact]
	public async Task GetCurrentBranchAsync_ForABranchNamedLikeAFolderOfBranches_KeepsTheWholeName()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("checkout", "--quiet", "-b", "release/heads/one");

		Assert.Equal("release/heads/one", await repository.Git.GetCurrentBranchAsync(repository.Folder, Token));
	}

	[Fact]
	public async Task GetCurrentBranchAsync_ForADetachedHead_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("tag", "main");
		await repository.RunGitAsync("checkout", "--quiet", "--detach");

		Assert.Null(await repository.Git.GetCurrentBranchAsync(repository.Folder, Token));
	}

	[Fact]
	public async Task ListBranchesAsync_WhenTagsHaveTheBranchNames_ListsThePlainBranchNames()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("feature");
		await repository.RunGitAsync("tag", "main");
		await repository.RunGitAsync("tag", "feature");

		IReadOnlyList<GitBranch> branches = await repository.Git.ListBranchesAsync(repository.Folder, Token);

		Assert.Equal(["feature", "main"], branches.Select(branch => branch.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task ListBranchesAsync_KeepsTheWholeNameOfBranchesInFolders()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("pyxie/card-1");
		await repository.CreateBranchAsync("release/2030/spring");

		IReadOnlyList<GitBranch> branches = await repository.Git.ListBranchesAsync(repository.Folder, Token);

		Assert.Equal(["main", "pyxie/card-1", "release/2030/spring"], branches.Select(branch => branch.Name).Order(StringComparer.Ordinal));
	}
}
