using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientDiscoveryTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task FindTopLevelAsync_FromASubfolder_ReturnsTheTopOfTheWorkingTree()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.WriteFileAsync("src/deep/file.txt", "content");

		string? top = await repository.Git.FindTopLevelAsync(Path.Combine(repository.Folder, "src", "deep"), Token);

		Assert.Equal(Path.GetFullPath(repository.Folder), top, ignoreCase: true);
	}

	[Fact]
	public async Task FindTopLevelAsync_FromTheTopItself_ReturnsIt()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		string? top = await repository.Git.FindTopLevelAsync(repository.Folder, Token);

		Assert.Equal(Path.GetFullPath(repository.Folder), top, ignoreCase: true);
	}

	[Fact]
	public async Task FindTopLevelAsync_OutsideAnyRepository_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using ScratchFolder plain = ScratchFolder.Create("pyxie-plain");

		Assert.Null(await repository.Git.FindTopLevelAsync(plain.Folder, Token));
	}

	[Fact]
	public async Task FindTopLevelAsync_ForAFolderThatDoesNotExist_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Null(await repository.Git.FindTopLevelAsync(Path.Combine(repository.Folder, "missing"), Token));
	}

	[Fact]
	public async Task GetCurrentBranchAsync_ReturnsTheCheckedOutBranch()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Equal("main", await repository.Git.GetCurrentBranchAsync(repository.Folder, Token));

		await repository.RunGitAsync("checkout", "--quiet", "-b", "feature/login");

		Assert.Equal("feature/login", await repository.Git.GetCurrentBranchAsync(repository.Folder, Token));
	}

	[Fact]
	public async Task GetCurrentBranchAsync_WithADetachedHead_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("checkout", "--quiet", "--detach");

		Assert.Null(await repository.Git.GetCurrentBranchAsync(repository.Folder, Token));
	}

	[Fact]
	public async Task GetCurrentBranchAsync_OutsideAnyRepository_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using ScratchFolder plain = ScratchFolder.Create("pyxie-plain");

		Assert.Null(await repository.Git.GetCurrentBranchAsync(plain.Folder, Token));
	}

	[Theory]
	[InlineData("main", true)]
	[InlineData("feature/login", true)]
	[InlineData("mai", false)]
	[InlineData("missing", false)]
	[InlineData("release-tag", false)]
	[InlineData("refs/heads/main", false)]
	public async Task BranchExistsAsync_IsTrueOnlyForALocalBranchOfThatExactName(string name, bool expected)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("feature/login");
		await repository.RunGitAsync("tag", "release-tag");

		bool exists = await repository.Git.BranchExistsAsync(repository.Folder, name, Token);

		Assert.True(exists == expected, $"BranchExistsAsync(\"{name}\") should be {expected}: only refs/heads/{name} counts, not a tag or a prefix.");
	}

	[Theory]
	[InlineData("main", true)]
	[InlineData("feature/login", true)]
	[InlineData("pyxie/card-12", true)]
	[InlineData("release-1.0", true)]
	[InlineData("has space", false)]
	[InlineData("a..b", false)]
	[InlineData("-leading", false)]
	[InlineData("trailing.lock", false)]
	[InlineData("tilde~1", false)]
	[InlineData("caret^", false)]
	[InlineData("colon:name", false)]
	[InlineData("ends/", false)]
	[InlineData("", false)]
	public async Task IsValidBranchNameAsync_AcceptsOnlyNamesGitAcceptsForABranch(string name, bool expected)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		bool valid = await repository.Git.IsValidBranchNameAsync(repository.Folder, name, Token);

		Assert.True(valid == expected, $"IsValidBranchNameAsync(\"{name}\") should be {expected}.");
	}

	[Fact]
	public async Task ListBranchesAsync_ListsEveryLocalBranchMostRecentlyCommittedFirstWithItsTip()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		DateTimeOffset oldTime = new(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
		DateTimeOffset newTime = new(2031, 5, 6, 7, 8, 9, TimeSpan.FromHours(-5));

		await repository.CreateBranchAsync("old");
		await repository.CheckoutAsync("old");
		string oldTip = await DatedCommits.CommitFileAtAsync(repository, "old.txt", "old", oldTime, "Old work");
		await repository.CheckoutAsync("main");
		await repository.CreateBranchAsync("new");
		await repository.CheckoutAsync("new");
		string newTip = await DatedCommits.CommitFileAtAsync(repository, "new.txt", "new", newTime, "New work");
		await repository.CheckoutAsync("main");
		string mainTip = await repository.GetHashAsync("main");

		IReadOnlyList<GitBranch> branches = await repository.Git.ListBranchesAsync(repository.Folder, Token);

		Assert.Equal(["new", "main", "old"], branches.Select(branch => branch.Name));
		Assert.Equal([newTip, mainTip, oldTip], branches.Select(branch => branch.Sha));
		Assert.Equal("New work", branches[0].Subject);
		Assert.Equal("Initial commit", branches[1].Subject);
		Assert.Equal("Old work", branches[2].Subject);
		Assert.Equal(new DateTime(2031, 5, 6, 12, 8, 9, DateTimeKind.Utc), branches[0].CommittedUtc);
		Assert.Equal(new DateTime(2020, 1, 2, 1, 4, 5, DateTimeKind.Utc), branches[2].CommittedUtc);
		Assert.All(branches, branch => Assert.Equal(DateTimeKind.Utc, branch.CommittedUtc.Kind));
	}

	[Fact]
	public async Task ListBranchesAsync_DoesNotListTagsOrRemoteTrackingBranches()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await origin.CreateBranchAsync("elsewhere");
		await using ClonedRepository clone = await ClonedRepository.CreateAsync(origin);
		await clone.RunGitAsync("tag", "release-tag");

		IReadOnlyList<GitBranch> branches = await origin.Git.ListBranchesAsync(clone.Folder, Token);

		Assert.Equal(["main"], branches.Select(branch => branch.Name));
	}

	[Fact]
	public async Task ResolveCommitAsync_ReturnsTheFullHashOfABranchAbbreviatedHashAndTags()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("a.txt", "a");
		await repository.RunGitAsync("tag", "light");
		await repository.RunGitAsync("tag", "-a", "annotated", "-m", "An annotated tag");

		Assert.Equal(tip, await repository.Git.ResolveCommitAsync(repository.Folder, "main", Token));
		Assert.Equal(tip, await repository.Git.ResolveCommitAsync(repository.Folder, "refs/heads/main", Token));
		Assert.Equal(tip, await repository.Git.ResolveCommitAsync(repository.Folder, tip[..10], Token));
		Assert.Equal(tip, await repository.Git.ResolveCommitAsync(repository.Folder, "light", Token));
		Assert.Equal(tip, await repository.Git.ResolveCommitAsync(repository.Folder, "annotated", Token));
		Assert.Equal(await repository.GetHashAsync("main~1"), await repository.Git.ResolveCommitAsync(repository.Folder, "main~1", Token));
	}

	[Theory]
	[InlineData("no-such-branch")]
	[InlineData("main:README.md")]
	[InlineData("--help")]
	[InlineData("-x")]
	public async Task ResolveCommitAsync_ForAnythingThatIsNotACommit_ReturnsNull(string revision)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Null(await repository.Git.ResolveCommitAsync(repository.Folder, revision, Token));
	}

	[Fact]
	public async Task RevParseAsync_ReturnsTheHashOfARevisionThatExists()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("a.txt", "a");

		Assert.Equal(tip, await repository.Git.RevParseAsync(repository.Folder, "main", Token));
		Assert.Equal(await repository.RunGitAsync("rev-parse", "main^{tree}"), await repository.Git.RevParseAsync(repository.Folder, "main^{tree}", Token));
	}

	[Fact]
	public async Task RevParseAsync_ForARevisionThatDoesNotExist_ThrowsAGitExceptionNamingTheCommand()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.RevParseAsync(repository.Folder, "no-such-branch", Token));

		Assert.Contains("git rev-parse failed", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetCommonDirectoryAsync_ForAWorkingTreeOrOneOfItsFolders_IsTheGitFolder()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.WriteFileAsync("src/file.txt", "content");
		string expected = Path.GetFullPath(Path.Combine(repository.Folder, ".git"));

		Assert.Equal(expected, await repository.Git.GetCommonDirectoryAsync(repository.Folder, Token), ignoreCase: true);
		Assert.Equal(expected, await repository.Git.GetCommonDirectoryAsync(Path.Combine(repository.Folder, "src"), Token), ignoreCase: true);
	}

	[Fact]
	public async Task GetCommonDirectoryAsync_ForABareRepository_IsTheRepositoryItself()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);

		Assert.Equal(Path.GetFullPath(hub.Folder), await hub.Repository.Git.GetCommonDirectoryAsync(hub.Folder, Token), ignoreCase: true);
	}
}
