using System.Text;

using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientReadFileTests
{
	private const int NoLimit = 1_000_000;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadFileAsync_ForATextFile_ReturnsItsTextAndSize()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("docs/guide.md", "first line\nsecond line");

		GitFileContent? content = await repository.Git.ReadFileAsync(repository.Folder, "main", "docs/guide.md", NoLimit, Token);

		Assert.NotNull(content);
		Assert.Equal("docs/guide.md", content.Path);
		Assert.Equal("first line\nsecond line".Length, content.Size);
		Assert.False(content.IsBinary, "A text file is not binary.");
		Assert.False(content.IsTooLarge, "A small file is not too large.");
		Assert.Equal("first line\nsecond line", content.Text);
	}

	[Fact]
	public async Task ReadFileAsync_TrimsSlashesFromThePath()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("docs/guide.md", "guide");

		GitFileContent? content = await repository.Git.ReadFileAsync(repository.Folder, "main", "/docs/guide.md/", NoLimit, Token);

		Assert.NotNull(content);
		Assert.Equal("docs/guide.md", content.Path);
		Assert.Equal("guide", content.Text);
	}

	[Fact]
	public async Task ReadFileAsync_ReadsNonAsciiTextAndCountsItsSizeInBytes()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		const string text = "naïve café ✓";
		await repository.CommitFileAsync("notes.txt", text);

		GitFileContent? content = await repository.Git.ReadFileAsync(repository.Folder, "main", "notes.txt", NoLimit, Token);

		Assert.NotNull(content);
		Assert.Equal(text, content.Text);
		Assert.Equal(Encoding.UTF8.GetByteCount(text), content.Size);
	}

	[Fact]
	public async Task ReadFileAsync_ForAFileWithANulByte_IsBinaryWithoutText()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("image.bin", "PNG\0data");

		GitFileContent? content = await repository.Git.ReadFileAsync(repository.Folder, "main", "image.bin", NoLimit, Token);

		Assert.NotNull(content);
		Assert.True(content.IsBinary, "A file with a NUL byte is binary.");
		Assert.False(content.IsTooLarge, "A small binary file is not too large.");
		Assert.Null(content.Text);
		Assert.Equal(8, content.Size);
	}

	[Fact]
	public async Task ReadFileAsync_ForAFileLargerThanTheLimit_IsTooLargeWithoutText()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("big.txt", "hello world");

		GitFileContent? content = await repository.Git.ReadFileAsync(repository.Folder, "main", "big.txt", 10, Token);

		Assert.NotNull(content);
		Assert.True(content.IsTooLarge, "An 11-byte file is larger than a 10-byte limit.");
		Assert.False(content.IsBinary, "A file that was not read is not reported binary.");
		Assert.Null(content.Text);
		Assert.Equal(11, content.Size);
	}

	[Fact]
	public async Task ReadFileAsync_ForAFileExactlyAsLargeAsTheLimit_ReadsIt()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("exact.txt", "hello world");

		GitFileContent? content = await repository.Git.ReadFileAsync(repository.Folder, "main", "exact.txt", 11, Token);

		Assert.NotNull(content);
		Assert.False(content.IsTooLarge, "A file whose size equals the limit is still read.");
		Assert.Equal("hello world", content.Text);
	}

	[Fact]
	public async Task ReadFileAsync_ReadsTheFileAsItWasAtTheRevision()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		await repository.CommitFileAsync(TempGitRepository.InitialFile, "rewritten");

		GitFileContent? before = await repository.Git.ReadFileAsync(repository.Folder, initial, TempGitRepository.InitialFile, NoLimit, Token);
		GitFileContent? after = await repository.Git.ReadFileAsync(repository.Folder, "main", TempGitRepository.InitialFile, NoLimit, Token);

		Assert.Equal(TempGitRepository.InitialContent, before?.Text);
		Assert.Equal("rewritten", after?.Text);
	}

	[Theory]
	[InlineData("main", "missing.txt")]
	[InlineData("main", "docs")]
	[InlineData("main", "docs/")]
	[InlineData("no-such-branch", "docs/guide.md")]
	public async Task ReadFileAsync_ForAPathThatIsNotAFileAtTheRevision_ReturnsNull(string revision, string path)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("docs/guide.md", "guide");

		Assert.Null(await repository.Git.ReadFileAsync(repository.Folder, revision, path, NoLimit, Token));
	}
}
