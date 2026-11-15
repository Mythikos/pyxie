using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientCommitDetailTests
{
	private const int NoLimit = 1_000_000;
	private const string Message = "Change several files\n\nFirst paragraph.\nSecond line.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetCommitAsync_ReturnsTheCommitAndItsFullMessage()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await CommitSeveralChangesAsync(repository);

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, tip, NoLimit, Token);

		Assert.NotNull(detail);
		Assert.Equal(tip, detail.Commit.Sha);
		Assert.Equal("Change several files", detail.Commit.Subject);
		Assert.Equal(Message, detail.Message);
		Assert.Equal(TempGitRepository.AuthorName, detail.Commit.AuthorName);
		Assert.Single(detail.Commit.ParentShas);
	}

	[Fact]
	public async Task GetCommitAsync_ListsAddedModifiedDeletedRenamedAndBinaryFilesWithTheirLineCounts()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await CommitSeveralChangesAsync(repository);

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, tip, NoLimit, Token);

		Assert.NotNull(detail);
		Assert.Equal(5, detail.Files.Count);
		Assert.Equal(new GitChangedFile("added.txt", null, 3, 0), detail.Files.Single(file => file.Path == "added.txt"));
		Assert.Equal(new GitChangedFile("modified.txt", null, 1, 1), detail.Files.Single(file => file.Path == "modified.txt"));
		Assert.Equal(new GitChangedFile("deleted.txt", null, 0, 2), detail.Files.Single(file => file.Path == "deleted.txt"));
		Assert.Equal(new GitChangedFile("new-name.txt", "old-name.txt", 0, 0), detail.Files.Single(file => file.Path == "new-name.txt"));
		Assert.Equal(new GitChangedFile("binary.bin", null, null, null), detail.Files.Single(file => file.Path == "binary.bin"));
	}

	[Fact]
	public async Task GetCommitAsync_ReturnsTheDiffOfTheCommit()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await CommitSeveralChangesAsync(repository);

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, tip, NoLimit, Token);

		Assert.NotNull(detail);
		Assert.Contains("diff --git a/added.txt b/added.txt", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("+++ b/added.txt", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("-two", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("+TWO", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("rename from old-name.txt", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("rename to new-name.txt", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("deleted file mode", detail.Patch, StringComparison.Ordinal);
		Assert.Contains("binary.bin differ", detail.Patch, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetCommitAsync_ForTheRootCommit_ShowsEveryFileItAddedAndNoParent()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, "main", NoLimit, Token);

		Assert.NotNull(detail);
		Assert.Empty(detail.Commit.ParentShas);
		Assert.Equal([new GitChangedFile(TempGitRepository.InitialFile, null, 1, 0)], detail.Files);
		Assert.Contains($"+{TempGitRepository.InitialContent}", detail.Patch, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetCommitAsync_ReturnsThePathsOfFilesWithSpacesAndNonAsciiNames()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.WriteFileAsync("dossier/résumé final.txt", "text");
		await repository.CommitAsync("Add an oddly named file");

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, "main", NoLimit, Token);

		Assert.NotNull(detail);
		Assert.Equal(["dossier/résumé final.txt"], detail.Files.Select(file => file.Path));
	}

	[Fact]
	public async Task GetCommitAsync_CutsADiffLongerThanTheLimit()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("long.txt", string.Join('\n', Enumerable.Range(1, 300).Select(line => $"line number {line}")));

		GitCommitDetail? full = await repository.Git.GetCommitAsync(repository.Folder, tip, NoLimit, Token);
		GitCommitDetail? cut = await repository.Git.GetCommitAsync(repository.Folder, tip, 600, Token);

		Assert.NotNull(full);
		Assert.NotNull(cut);
		Assert.DoesNotContain("truncated", full.Patch, StringComparison.Ordinal);
		Assert.Contains("truncated", cut.Patch, StringComparison.Ordinal);
		Assert.True(cut.Patch.Length < full.Patch.Length / 2, $"A 600-character limit should shorten a {full.Patch.Length}-character diff a lot, but it has {cut.Patch.Length}.");
		Assert.StartsWith("diff --git", cut.Patch, StringComparison.Ordinal);
		Assert.Equal(full.Files, cut.Files);
	}

	[Fact]
	public async Task GetCommitAsync_AcceptsABranchNameAndAnAbbreviatedHash()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("a.txt", "a");

		GitCommitDetail? byBranch = await repository.Git.GetCommitAsync(repository.Folder, "main", NoLimit, Token);
		GitCommitDetail? byAbbreviation = await repository.Git.GetCommitAsync(repository.Folder, tip[..10], NoLimit, Token);

		Assert.Equal(tip, byBranch?.Commit.Sha);
		Assert.Equal(tip, byAbbreviation?.Commit.Sha);
	}

	[Theory]
	[InlineData("no-such-commit")]
	[InlineData("main:README.md")]
	[InlineData("--help")]
	public async Task GetCommitAsync_ForAnythingThatIsNotACommit_ReturnsNull(string revision)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Null(await repository.Git.GetCommitAsync(repository.Folder, revision, NoLimit, Token));
	}

	// Commits one change of every kind on top of the files set up first.
	private static async Task<string> CommitSeveralChangesAsync(TempGitRepository repository)
	{
		await repository.WriteFileAsync("modified.txt", "one\ntwo\nthree");
		await repository.WriteFileAsync("old-name.txt", "rename me\nsecond line\nthird line");
		await repository.WriteFileAsync("deleted.txt", "bye\nbye again");
		await repository.CommitAsync("Add the files to change");

		await repository.WriteFileAsync("added.txt", "a\nb\nc");
		await repository.WriteFileAsync("modified.txt", "one\nTWO\nthree");
		await repository.WriteFileAsync("binary.bin", "PNG\0data");
		await repository.RunGitAsync("mv", "old-name.txt", "new-name.txt");
		await repository.RunGitAsync("rm", "--quiet", "deleted.txt");

		return await repository.CommitAsync(Message);
	}
}
