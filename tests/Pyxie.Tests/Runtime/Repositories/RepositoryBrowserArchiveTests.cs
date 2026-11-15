using System.IO.Compression;

using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

public sealed class RepositoryBrowserArchiveTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ArchiveAsync_ZipsTheFilesOfTheBranchInsideAFolderNamedAfterTheRepository()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CommitFileAsync("docs/notes.txt", "notes");
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		string name = Path.GetFileName(source.Folder);

		RepositoryArchive? archive = await rig.Browser.ArchiveAsync("main", Token);

		Assert.NotNull(archive);
		await using (archive.Content)
		{
			Assert.Equal($"{name}-main.zip", archive.FileName);
			using ZipArchive zip = new(archive.Content, ZipArchiveMode.Read);
			Assert.All(zip.Entries, entry => Assert.StartsWith($"{name}/", entry.FullName, StringComparison.Ordinal));
			Assert.Equal([$"{name}/{TempGitRepository.InitialFile}", $"{name}/docs/notes.txt"], zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).Select(entry => entry.FullName).Order(StringComparer.Ordinal));
			Assert.Equal(TempGitRepository.InitialContent, await ReadEntryAsync(zip, $"{name}/{TempGitRepository.InitialFile}"));
			Assert.Equal("notes", await ReadEntryAsync(zip, $"{name}/docs/notes.txt"));
		}
	}

	[Fact]
	public async Task ArchiveAsync_ForABranchWhoseNameHasASlash_ZipsThatBranchAndNamesTheFileWithADash()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.RunGitAsync("checkout", "--quiet", "-b", "feature/login");
		await source.CommitFileAsync("login.txt", "only on the feature");
		await source.CheckoutAsync("main");
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));
		string name = Path.GetFileName(source.Folder);

		RepositoryArchive? archive = await rig.Browser.ArchiveAsync("feature/login", Token);

		Assert.NotNull(archive);
		await using (archive.Content)
		{
			Assert.Equal($"{name}-feature-login.zip", archive.FileName);
			using ZipArchive zip = new(archive.Content, ZipArchiveMode.Read);
			Assert.Equal("only on the feature", await ReadEntryAsync(zip, $"{name}/login.txt"));
		}
	}

	[Theory]
	[InlineData("no-such-branch")]
	[InlineData("origin/main")]
	[InlineData("HEAD")]
	public async Task ArchiveAsync_ForAnUnknownBranch_IsNull(string branch)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		Assert.Null(await rig.Browser.ArchiveAsync(branch, Token));
	}

	[Fact]
	public async Task ArchiveAsync_WhenNoRepositoryIsConnected_IsNull()
	{
		await using RepositoryRig rig = new();

		Assert.Null(await rig.Browser.ArchiveAsync("main", Token));
	}

	[Fact]
	public async Task ArchiveAsync_DeletesItsTemporaryFileWhenTheContentIsDisposed()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		RepositoryArchive? archive = await rig.Browser.ArchiveAsync("main", Token);

		Assert.NotNull(archive);
		string file = Assert.IsType<FileStream>(archive.Content).Name;
		Assert.True(File.Exists(file), "The zip is a file while it is being served.");

		await archive.Content.DisposeAsync();

		Assert.False(File.Exists(file), "Disposing the content must delete the temporary file.");
	}

	private static async Task<string> ReadEntryAsync(ZipArchive zip, string path)
	{
		ZipArchiveEntry entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"The zip has no entry {path}.");
		await using Stream stream = await entry.OpenAsync(Token);
		using StreamReader reader = new(stream);

		return await reader.ReadToEndAsync(Token);
	}
}
