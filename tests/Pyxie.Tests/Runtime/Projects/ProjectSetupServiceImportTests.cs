using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>Importing a local folder: Pyxie clones the folder's git working copy into the hub of a new project folder and works only in the hub, so the folder itself is never changed.</summary>
public sealed class ProjectSetupServiceImportTests
{
	private const string ExcludedPattern = "/.pyxie/";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\"\"")]
	public async Task ImportFolderAsync_WithoutAFolder_AsksForOne(string path)
	{
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(path, Token));

		Assert.Equal("Enter the folder of a git repository.", error);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Theory]
	[InlineData("relative-folder")]
	[InlineData("..\\somewhere")]
	[InlineData("C:drive-relative")]
	public async Task ImportFolderAsync_WithARelativePath_AsksForTheFullPath(string path)
	{
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(path, Token));

		Assert.Equal("Enter the folder's full path.", error);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task ImportFolderAsync_ForAFolderThatDoesNotExist_SaysSo()
	{
		await using RepositoryRig rig = new();
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-plain");
		string missing = scratch.Combine("missing");

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(missing, Token));

		Assert.Equal($"The folder {missing} does not exist.", error);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task ImportFolderAsync_ForAFolderOutsideAnyRepository_SaysItIsNotInOne()
	{
		await using RepositoryRig rig = new();
		await using ScratchFolder plain = ScratchFolder.Create("pyxie-plain");

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(plain.Folder, Token));

		Assert.Equal($"The folder {plain.Folder} is not in a git repository.", error);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task ImportFolderAsync_ForARepositoryFolder_ClonesItIntoANewOpenProjectNamedAfterItAndStoresTheFolderAsItsOrigin()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		string folder = rig.ProjectFolderFor(repository.Folder);
		Assert.Equal(rig.HubFor(repository.Folder), connected.Path);
		Assert.StartsWith(rig.Layout.ProjectsFolder, connected.Path, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(RepositorySource.Folder, connected.Source);
		Assert.Equal(Path.GetFullPath(repository.Folder), connected.Origin, ignoreCase: true);
		Assert.Equal("main", connected.TargetBranch);
		Project open = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
		Assert.Equal((Path.GetFileName(repository.Folder), folder), (open.Name, open.Folder));
		Assert.True(File.Exists(Path.Combine(folder, "pyxie.db")), "The project's database is created in its folder.");
		Assert.Equal([open.Id], (await rig.Catalog.ListAsync(Token)).Select(project => project.Id));
		Repository stored = await GetStoredAsync(rig);
		Assert.Equal((connected.Id, connected.Source, connected.Path, connected.Origin, connected.TargetBranch), (stored.Id, stored.Source, stored.Path, stored.Origin, stored.TargetBranch));
	}

	[Fact]
	public async Task ImportFolderAsync_RecordsTheFolderAsTheSourceOfTheProjectInTheMachineDatabase()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Project listed = Assert.Single(await rig.Catalog.ListAsync(Token));
		Assert.Equal(connected.Origin, listed.Source);
		Assert.Equal(Path.GetFullPath(repository.Folder), Path.GetFullPath(listed.Source), ignoreCase: true);
	}

	[Fact]
	public async Task ImportFolderAsync_MakesARealCloneThatRecordsTheFolderAsItsOriginButKeepsNoWayBackToItAndWhoseCommitsAreTheFolders()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("docs/notes.txt", "from the source");
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Equal(Path.GetFullPath(repository.Folder), Path.GetFullPath(connected.Origin), ignoreCase: true);
		Assert.False((await repository.TryRunGitAsync("-C", connected.Path, "config", "--get", "remote.origin.url")).Succeeded, "The hub keeps no URL for the folder, so a push from the hub has nowhere to go.");
		Assert.False(string.Equals(Path.GetFullPath(repository.Folder), Path.GetFullPath(connected.Path), StringComparison.OrdinalIgnoreCase), "The clone is not the folder that was imported.");
		Assert.Equal("true", await repository.RunGitAsync("-C", connected.Path, "rev-parse", "--is-bare-repository"));
		Assert.Equal(TempGitRepository.InitialContent, await rig.ReadFileAtAsync(connected.Path, "HEAD", TempGitRepository.InitialFile));
		Assert.Equal("from the source", await rig.ReadFileAtAsync(connected.Path, "HEAD", "docs/notes.txt"));
		Assert.Equal(await repository.GetHashAsync("main"), await rig.Git.RevParseAsync(connected.Path, "HEAD", Token));
		Assert.Equal("main", await rig.Git.GetCurrentBranchAsync(connected.Path, Token));
	}

	[Fact]
	public async Task ImportFolderAsync_TakesTheBranchThatIsCheckedOutAsTheTargetBranchAndPointsTheClonesHeadAtIt()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("checkout", "--quiet", "-b", "develop");
		await repository.CommitFileAsync("develop-only.txt", "only on develop");
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Equal("develop", connected.TargetBranch);
		Assert.Equal("develop", await rig.Git.GetCurrentBranchAsync(connected.Path, Token));
		Assert.Equal("only on develop", await rig.ReadFileAtAsync(connected.Path, "HEAD", "develop-only.txt"));
	}

	[Fact]
	public async Task ImportFolderAsync_ForASubfolder_ImportsTheWholeWorkingTreeAndStoresItsTopAsTheOrigin()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("src/deep/file.txt", "content");
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(Path.Combine(repository.Folder, "src", "deep"), Token));

		Assert.Equal(Path.GetFullPath(repository.Folder), connected.Origin, ignoreCase: true);
		Assert.Equal(rig.HubFor(repository.Folder), connected.Path);
		Assert.Equal(TempGitRepository.InitialContent, await rig.ReadFileAtAsync(connected.Path, "HEAD", TempGitRepository.InitialFile));
		Assert.Equal("content", await rig.ReadFileAtAsync(connected.Path, "HEAD", "src/deep/file.txt"));
	}

	[Fact]
	public async Task ImportFolderAsync_AcceptsAPathPastedWithQuotesAndSpaces()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync($"  \"{repository.Folder}\"  ", Token));

		Assert.Equal(Path.GetFullPath(repository.Folder), connected.Origin, ignoreCase: true);
	}

	[Fact]
	public async Task ImportFolderAsync_LeavesTheSourcesExcludeFileAlone()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(repository);
		await using RepositoryRig rig = new();

		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.DoesNotContain(ExcludedPattern, await ReadExcludeLinesAsync(repository));
		await before.AssertUnchangedAsync(repository);
	}

	[Fact]
	public async Task ImportFolderAsync_LeavesTheSourceFolderExactlyAsItWasBranchesHeadFilesAndStatus()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("feature/other");
		await repository.CommitFileAsync("notes.txt", "line one\r\nline two\nline three");
		await repository.WriteFileAsync("notes.txt", "edited after the commit\r\nwith mixed\nline endings\r\n");
		await repository.WriteFileAsync("untracked.bin", "\0\u0001\u0002 not text");
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(repository);
		await using RepositoryRig rig = new();

		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		await before.AssertUnchangedAsync(repository);
		Assert.Equal(["feature/other", "main"], (await rig.Git.ListBranchesAsync(repository.Folder, Token)).Select(branch => branch.Name).Order(StringComparer.Ordinal));
		Assert.Equal("main", await repository.GetCurrentBranchAsync());
	}

	[Fact]
	public async Task ImportFolderAsync_ImportsTheCommittedStateAndLeavesUncommittedChangesInTheFolder()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("tracked.txt", "committed");
		await repository.WriteFileAsync("tracked.txt", "edited but not committed");
		await repository.WriteFileAsync("staged.txt", "staged but not committed");
		await repository.RunGitAsync("add", "staged.txt");
		await repository.WriteFileAsync("untracked.txt", "never added");
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(repository);
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Equal("committed", await rig.ReadFileAtAsync(connected.Path, "HEAD", "tracked.txt"));
		Assert.Null(await rig.ReadFileAtAsync(connected.Path, "HEAD", "staged.txt"));
		Assert.Null(await rig.ReadFileAtAsync(connected.Path, "HEAD", "untracked.txt"));
		await before.AssertUnchangedAsync(repository);
	}

	[Fact]
	public async Task ImportFolderAsync_WithADetachedHead_IsRefusedNamingTheFolderAndLeavesNothingBehind()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("checkout", "--quiet", "--detach");
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(repository);
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Equal($"No branch is checked out in {Path.GetFullPath(repository.Folder)}. Check out the branch finished cards should be merged into, then import it.", error, ignoreCase: true);
		Assert.Null(await rig.GetStoredAsync());
		Assert.DoesNotContain(ExcludedPattern, await ReadExcludeLinesAsync(repository));
		await rig.AssertNothingImportedAsync();
		await before.AssertUnchangedAsync(repository);
	}

	[Fact]
	public async Task ImportFolderAsync_ForABranchWithNoCommits_IsRefusedNamingTheBranchAndTheFolderAndLeavesNothingBehind()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.RunGitAsync("checkout", "--quiet", "--orphan", "unborn");
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Equal($"The branch unborn in {Path.GetFullPath(repository.Folder)} has no commits yet. Make a first commit, then import it.", error, ignoreCase: true);
		Assert.Null(await rig.GetStoredAsync());
		Assert.DoesNotContain(ExcludedPattern, await ReadExcludeLinesAsync(repository));
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task ImportFolderAsync_ForAnEmptyRepository_IsRefusedBecauseItHasNoCommitsAndLeavesNothingBehind()
	{
		await using EmptyGitRepository repository = await EmptyGitRepository.CreateAsync();
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Contains("has no commits yet", error, StringComparison.Ordinal);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task ImportFolderAsync_ForAFolderInsideTheWorkspaceRoot_IsRefused()
	{
		await using TempGitRepository helper = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		string inside = Path.Combine(rig.Layout.Root, "clones", "inner");
		await helper.RunGitAsync("init", "--quiet", "-b", "main", inside);
		Directory.CreateDirectory(Path.Combine(inside, "sub"));

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(Path.Combine(inside, "sub"), Token));

		Assert.Equal("That folder is inside Pyxie's own workspace folder.", error);
		Assert.Null(await rig.GetStoredAsync());
		Assert.Equal([Path.Combine(rig.Layout.Root, "clones")], Directory.GetDirectories(rig.Layout.Root));
	}

	[Fact]
	public async Task ImportFolderAsync_ForARepositoryWhoseNameOnlyStartsLikeTheWorkspaceRoot_IsAllowed()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string rootThatIsAPrefixOfTheRepositoryName = repository.Folder[..^1];

		try
		{
			await using RepositoryRig rig = new(rootThatIsAPrefixOfTheRepositoryName);

			Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

			Assert.Equal(Path.GetFullPath(repository.Folder), connected.Origin, ignoreCase: true);
			Assert.Equal(rig.HubFor(repository.Folder), connected.Path);
		}
		finally
		{
			// The rig deletes only a workspace root it made itself, and the clone was made under this one.
			await TempFolder.DeleteAsync(rootThatIsAPrefixOfTheRepositoryName);
		}
	}

	[Fact]
	public async Task ImportFolderAsync_WhileAProjectIsOpen_IsRefusedKeepsTheOpenProjectAndClonesNothingMore()
	{
		await using TempGitRepository first = await TempGitRepository.CreateAsync();
		await using TempGitRepository second = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(first.Folder, Token));

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(second.Folder, Token));

		Assert.Equal("A project is open. Close it to import another.", error);
		Repository stored = await GetStoredAsync(rig);
		Assert.Equal(Path.GetFullPath(first.Folder), stored.Origin, ignoreCase: true);
		Assert.Equal(rig.HubFor(first.Folder), stored.Path);
		Assert.Equal([rig.ProjectFolderFor(first.Folder)], Directory.GetDirectories(rig.Layout.ProjectsFolder));
		Assert.Single(await rig.Catalog.ListAsync(Token));
	}

	[Fact]
	public async Task ImportFolderAsync_WhenTheConnectionIsRefusedAfterTheProjectOpened_ClosesAndRemovesTheProjectDeletesItsFolderAndLeavesTheSourceAlone()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		RefusingRepositoryService repositories = new();
		await using RepositoryRig rig = new(repositories: repositories);
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(repository);

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		Assert.Equal(RefusingRepositoryService.Reason, error);
		Assert.True(repositories.CloneExistedWhenAsked, "The import cloned before the connection was refused, so the hub is what had to be removed.");
		RepositoryInput asked = repositories.Asked ?? throw new InvalidOperationException("The connection was never asked for.");
		Assert.Equal((RepositorySource.Folder, rig.HubFor(repository.Folder), "main"), (asked.Source, asked.Path, asked.TargetBranch));
		Assert.Equal(Path.GetFullPath(repository.Folder), asked.Origin, ignoreCase: true);
		Assert.False(Directory.Exists(rig.ProjectFolderFor(repository.Folder)), "The project's folder, with its hub and database, must be gone.");
		await rig.AssertNothingImportedAsync();
		await before.AssertUnchangedAsync(repository);
	}

	[Fact]
	public async Task ImportFolderAsync_WhenGitCannotCompleteTheClone_FailsWithGitsReasonAndLeavesNoFolderBehind()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		using FileStream? held = BlockObject(await GetLooseObjectPathAsync(repository, $"main:{TempGitRepository.InitialFile}"));
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.ImportFolderAsync(repository.Folder, Token));

		string prefix = $"git could not clone {Path.GetFullPath(repository.Folder)}: ";
		Assert.StartsWith(prefix, error, StringComparison.OrdinalIgnoreCase);
		Assert.True(error.Length > prefix.Length, "The message carries git's reason.");
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	private static async Task<Repository> GetStoredAsync(RepositoryRig rig)
	{
		Repository? stored = await rig.GetStoredAsync();
		Assert.NotNull(stored);

		return stored;
	}

	private static async Task<IReadOnlyList<string>> ReadExcludeLinesAsync(TempGitRepository repository)
	{
		string exclude = Path.Combine(repository.Folder, ".git", "info", "exclude");

		return File.Exists(exclude) ? await File.ReadAllLinesAsync(exclude, Token) : [];
	}

	private static async Task<string> GetLooseObjectPathAsync(TempGitRepository repository, string revision)
	{
		string hash = await repository.GetHashAsync(revision);
		string file = Path.Combine(repository.Folder, ".git", "objects", hash[..2], hash[2..]);
		Assert.True(File.Exists(file), $"The object {hash} must be loose in a new repository: {file}.");

		return file;
	}

	// Makes a loose object unreadable, so the repository still names its commit but a clone of it cannot copy the object: another process holds it open on Windows (the caller disposes the returned stream to let go of it), and the file has no permissions elsewhere.
	private static FileStream? BlockObject(string file)
	{
		if (OperatingSystem.IsWindows())
		{
			return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
		}

		File.SetUnixFileMode(file, UnixFileMode.None);

		return null;
	}
}
