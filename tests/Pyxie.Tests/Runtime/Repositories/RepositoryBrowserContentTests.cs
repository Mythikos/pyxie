using Pyxie.Data.Entities;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

public sealed class RepositoryBrowserContentTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ListAsync_ListsADirectoryAtARevision()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.WriteFileAsync("docs/guide.md", "guide");
		await repository.WriteFileAsync("docs/images/logo.txt", "logo");
		await repository.WriteFileAsync("zeta.txt", "zeta");
		await repository.CommitAsync("Add files");
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		IReadOnlyList<GitTreeEntry>? root = await rig.Browser.ListAsync(connected, "main", string.Empty, Token);
		IReadOnlyList<GitTreeEntry>? docs = await rig.Browser.ListAsync(connected, "main", "docs", Token);

		Assert.Equal(["docs", "README.md", "zeta.txt"], root?.Select(entry => entry.Name));
		Assert.Equal(["docs/images", "docs/guide.md"], docs?.Select(entry => entry.Path));
	}

	[Fact]
	public async Task ListAsync_ForAPathThatIsNotADirectory_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		Assert.Null(await rig.Browser.ListAsync(connected, "main", "missing", Token));
		Assert.Null(await rig.Browser.ListAsync(connected, "main", TempGitRepository.InitialFile, Token));
	}

	[Fact]
	public async Task ReadAsync_ReadsATextFileAtARevision()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		await repository.CommitFileAsync(TempGitRepository.InitialFile, "rewritten");
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		GitFileContent? before = await rig.Browser.ReadAsync(connected, initial, TempGitRepository.InitialFile, Token);
		GitFileContent? after = await rig.Browser.ReadAsync(connected, "main", TempGitRepository.InitialFile, Token);

		Assert.Equal(TempGitRepository.InitialContent, before?.Text);
		Assert.Equal("rewritten", after?.Text);
	}

	[Fact]
	public async Task ReadAsync_ReadsAFileUpToTheLimitAndReportsOneByteMoreAsTooLarge()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.WriteFileAsync("limit.txt", new string('a', RepositoryBrowser.MaxFileBytes));
		await repository.WriteFileAsync("over.txt", new string('b', RepositoryBrowser.MaxFileBytes + 1));
		await repository.CommitAsync("Add files at the limit");
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		GitFileContent? atLimit = await rig.Browser.ReadAsync(connected, "main", "limit.txt", Token);
		GitFileContent? overLimit = await rig.Browser.ReadAsync(connected, "main", "over.txt", Token);

		Assert.NotNull(atLimit);
		Assert.False(atLimit.IsTooLarge, "A file exactly at the limit is shown.");
		Assert.Equal(RepositoryBrowser.MaxFileBytes, atLimit.Text?.Length);
		Assert.NotNull(overLimit);
		Assert.True(overLimit.IsTooLarge, "A file one byte over the limit is not shown.");
		Assert.Null(overLimit.Text);
		Assert.Equal(RepositoryBrowser.MaxFileBytes + 1, overLimit.Size);
	}

	[Fact]
	public async Task ReadAsync_ForAPathThatIsNotAFile_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("docs/guide.md", "guide");
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		Assert.Null(await rig.Browser.ReadAsync(connected, "main", "missing.txt", Token));
		Assert.Null(await rig.Browser.ReadAsync(connected, "main", "docs", Token));
	}

	[Fact]
	public async Task GetHistoryAsync_PagesFiftyCommitsAtATimeNewestFirst()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await EmptyCommits.AddAsync(repository, "main", 120);
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		IReadOnlyList<GitCommit> first = await rig.Browser.GetHistoryAsync(connected, "main", 0, Token);
		IReadOnlyList<GitCommit> second = await rig.Browser.GetHistoryAsync(connected, "main", 1, Token);
		IReadOnlyList<GitCommit> third = await rig.Browser.GetHistoryAsync(connected, "main", 2, Token);
		IReadOnlyList<GitCommit> fourth = await rig.Browser.GetHistoryAsync(connected, "main", 3, Token);

		Assert.Equal(50, RepositoryBrowser.CommitsPerPage);
		Assert.Equal((50, EmptyCommits.SubjectOf(120), EmptyCommits.SubjectOf(71)), (first.Count, first[0].Subject, first[^1].Subject));
		Assert.Equal((50, EmptyCommits.SubjectOf(70), EmptyCommits.SubjectOf(21)), (second.Count, second[0].Subject, second[^1].Subject));
		Assert.Equal((21, EmptyCommits.SubjectOf(20), "Initial commit"), (third.Count, third[0].Subject, third[^1].Subject));
		Assert.Empty(fourth);
	}

	[Fact]
	public async Task GetHistoryAsync_ForANegativePage_ReturnsTheFirstPage()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await EmptyCommits.AddAsync(repository, "main", 60);
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		IReadOnlyList<GitCommit> first = await rig.Browser.GetHistoryAsync(connected, "main", 0, Token);
		IReadOnlyList<GitCommit> negative = await rig.Browser.GetHistoryAsync(connected, "main", -3, Token);

		Assert.Equal(first.Select(commit => commit.Sha), negative.Select(commit => commit.Sha));
		Assert.Equal(50, negative.Count);
	}

	[Fact]
	public async Task GetHistoryAsync_ForABranchOtherThanTheTarget_ShowsThatBranchsHistory()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("feature");
		await repository.CheckoutAsync("feature");
		await repository.CommitFileAsync("feature.txt", "feature work", "Feature work");
		await repository.CheckoutAsync("main");
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		IReadOnlyList<GitCommit> commits = await rig.Browser.GetHistoryAsync(connected, "feature", 0, Token);

		Assert.Equal(["Feature work", "Initial commit"], commits.Select(commit => commit.Subject));
	}

	[Fact]
	public async Task GetCommitAsync_ReturnsTheCommitWithItsMessageFilesAndDiff()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("docs/guide.md", "line one\nline two", "Add the guide\n\nIt explains things.");
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		GitCommitDetail? detail = await rig.Browser.GetCommitAsync(connected, tip, Token);

		Assert.NotNull(detail);
		Assert.Equal(tip, detail.Commit.Sha);
		Assert.Equal("Add the guide\n\nIt explains things.", detail.Message);
		Assert.Equal([new GitChangedFile("docs/guide.md", null, 2, 0)], detail.Files);
		Assert.Contains("+line two", detail.Patch, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetCommitAsync_ForAHashThatDoesNotExist_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		Assert.Null(await rig.Browser.GetCommitAsync(connected, "0123456789abcdef0123456789abcdef01234567", Token));
		Assert.Null(await rig.Browser.GetCommitAsync(connected, "not-a-commit", Token));
	}
}
