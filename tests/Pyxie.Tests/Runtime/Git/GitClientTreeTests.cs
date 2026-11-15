using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientTreeTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ListTreeAsync_ForTheRoot_ListsDirectoriesFirstThenFilesEachByNameIgnoringCase()
	{
		await using TempGitRepository repository = await CreateRepositoryWithFilesAsync();

		IReadOnlyList<GitTreeEntry>? entries = await repository.Git.ListTreeAsync(repository.Folder, "main", string.Empty, Token);

		Assert.NotNull(entries);
		Assert.Equal(["adir", "docs", "Zdir", "A.txt", "b.txt", "c.txt", "README.md", "with space.txt"], entries.Select(entry => entry.Name));
	}

	[Fact]
	public async Task ListTreeAsync_DescribesEachEntryWithItsKindPathAndSize()
	{
		await using TempGitRepository repository = await CreateRepositoryWithFilesAsync();

		IReadOnlyList<GitTreeEntry>? entries = await repository.Git.ListTreeAsync(repository.Folder, "main", string.Empty, Token);

		Assert.NotNull(entries);
		GitTreeEntry docs = entries.Single(entry => entry.Name == "docs");
		GitTreeEntry readme = entries.Single(entry => entry.Name == "README.md");
		GitTreeEntry spaced = entries.Single(entry => entry.Name == "with space.txt");
		Assert.Equal((GitTreeEntryKind.Directory, "docs", (long?)null), (docs.Kind, docs.Path, docs.Size));
		Assert.Equal((GitTreeEntryKind.File, "README.md", (long?)TempGitRepository.InitialContent.Length), (readme.Kind, readme.Path, readme.Size));
		Assert.Equal((GitTreeEntryKind.File, "with space.txt", (long?)"spaced name".Length), (spaced.Kind, spaced.Path, spaced.Size));
	}

	[Theory]
	[InlineData("docs")]
	[InlineData("docs/")]
	[InlineData("/docs/")]
	public async Task ListTreeAsync_ForASubfolder_ListsItsEntriesWithPathsFromTheRepositoryRoot(string path)
	{
		await using TempGitRepository repository = await CreateRepositoryWithFilesAsync();

		IReadOnlyList<GitTreeEntry>? entries = await repository.Git.ListTreeAsync(repository.Folder, "main", path, Token);

		Assert.NotNull(entries);
		Assert.Equal([("images", "docs/images", GitTreeEntryKind.Directory), ("guide.md", "docs/guide.md", GitTreeEntryKind.File)], entries.Select(entry => (entry.Name, entry.Path, entry.Kind)));
	}

	[Fact]
	public async Task ListTreeAsync_ForANestedSubfolder_ListsItsFiles()
	{
		await using TempGitRepository repository = await CreateRepositoryWithFilesAsync();

		IReadOnlyList<GitTreeEntry>? entries = await repository.Git.ListTreeAsync(repository.Folder, "main", "docs/images", Token);

		Assert.NotNull(entries);
		GitTreeEntry logo = Assert.Single(entries);
		Assert.Equal(("docs/images/logo.txt", GitTreeEntryKind.File), (logo.Path, logo.Kind));
	}

	[Fact]
	public async Task ListTreeAsync_ShowsTheTreeAsItWasAtTheRevision()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		await repository.CommitFileAsync("docs/guide.md", "guide");

		IReadOnlyList<GitTreeEntry>? before = await repository.Git.ListTreeAsync(repository.Folder, initial, string.Empty, Token);
		IReadOnlyList<GitTreeEntry>? after = await repository.Git.ListTreeAsync(repository.Folder, "main", string.Empty, Token);

		Assert.NotNull(before);
		Assert.NotNull(after);
		Assert.Equal(["README.md"], before.Select(entry => entry.Name));
		Assert.Equal(["docs", "README.md"], after.Select(entry => entry.Name));
		Assert.Null(await repository.Git.ListTreeAsync(repository.Folder, initial, "docs", Token));
	}

	[Theory]
	[InlineData("missing")]
	[InlineData("docs/missing")]
	[InlineData("README.md")]
	public async Task ListTreeAsync_ForAPathThatIsNotADirectory_ReturnsNull(string path)
	{
		await using TempGitRepository repository = await CreateRepositoryWithFilesAsync();

		Assert.Null(await repository.Git.ListTreeAsync(repository.Folder, "main", path, Token));
	}

	[Fact]
	public async Task ListTreeAsync_ForARevisionThatDoesNotExist_ReturnsNull()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Null(await repository.Git.ListTreeAsync(repository.Folder, "no-such-branch", string.Empty, Token));
	}

	[Fact]
	public async Task ListTreeAsync_ReportsAGitlinkAsASubmoduleWithoutASize()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string pointedAt = await repository.GetHashAsync();
		await repository.RunGitAsync("update-index", "--add", "--cacheinfo", $"160000,{pointedAt},vendor/lib");
		await repository.RunGitAsync("commit", "--quiet", "--message", "Add a submodule pointer");

		IReadOnlyList<GitTreeEntry>? entries = await repository.Git.ListTreeAsync(repository.Folder, "main", "vendor", Token);

		Assert.NotNull(entries);
		GitTreeEntry submodule = Assert.Single(entries);
		Assert.Equal(("vendor/lib", GitTreeEntryKind.Submodule, (long?)null), (submodule.Path, submodule.Kind, submodule.Size));
	}

	private static async Task<TempGitRepository> CreateRepositoryWithFilesAsync()
	{
		TempGitRepository repository = await TempGitRepository.CreateAsync();

		try
		{
			await repository.WriteFileAsync("b.txt", "bee");
			await repository.WriteFileAsync("A.txt", "ay");
			await repository.WriteFileAsync("c.txt", "see");
			await repository.WriteFileAsync("with space.txt", "spaced name");
			await repository.WriteFileAsync("docs/guide.md", "guide");
			await repository.WriteFileAsync("docs/images/logo.txt", "logo");
			await repository.WriteFileAsync("Zdir/a.txt", "z");
			await repository.WriteFileAsync("adir/b.txt", "a");
			await repository.CommitAsync("Add files and folders");

			return repository;
		}
		catch
		{
			await repository.DisposeAsync();

			throw;
		}
	}
}
