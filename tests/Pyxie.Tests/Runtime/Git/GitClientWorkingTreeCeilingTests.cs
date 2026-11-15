using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary><see cref="GitClient.GetCurrentBranchAsync"/> never looks above the folder it is given for a repository. A folder that is not a repository (any more) therefore reports no branch, instead of the branch of a repository that happens to contain it, while a repository in the folder itself, bare like the hub or not, is still found.</summary>
public sealed class GitClientWorkingTreeCeilingTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetCurrentBranchAsync_ForAFolderThatIsNotAWorkingTreeAnyMore_ReportsNoBranch()
	{
		await using TempGitRepository ancestor = await TempGitRepository.CreateAsync();
		await ancestor.WriteFileAsync("inner/inner.txt", "a folder that lost its .git");
		string inner = Path.Combine(ancestor.Folder, "inner");

		// Plain git, which does look above the folder, finds the surrounding repository and its branch.
		Assert.Equal("refs/heads/main", await ancestor.RunGitAsync("-C", inner, "symbolic-ref", "HEAD"));

		Assert.Null(await ancestor.Git.GetCurrentBranchAsync(inner, Token));
	}

	[Fact]
	public async Task GetCurrentBranchAsync_ForABareRepository_FindsItInTheFolderItself()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.RunGitAsync("checkout", "--quiet", "-b", "develop");
		await using BareRepository hub = await BareRepository.CloneAsync(source);

		Assert.Equal("develop", await hub.Repository.Git.GetCurrentBranchAsync(hub.Folder, Token));
	}
}
