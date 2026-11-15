using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

public sealed class ProjectSetupServiceCloneTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task CloneAsync_WithoutAUrl_AsksForOne(string url)
	{
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.CloneAsync(url, Token));

		Assert.Equal("Enter the URL to clone.", error);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_FromALocalRepositoryPath_ClonesIntoTheHubOfANewProjectAndStoresItAsTheOrigin()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.CloneAsync($"  {origin.Folder}  ", Token));

		string expectedPath = rig.HubFor(origin.Folder);
		Assert.Equal(expectedPath, connected.Path);
		Assert.Equal(RepositorySource.Url, connected.Source);
		Assert.Equal(origin.Folder, connected.Origin);
		Assert.Equal("main", connected.TargetBranch);
		Assert.Equal(rig.ProjectFolderFor(origin.Folder), rig.OpenProject.Current?.Folder);
		Assert.Equal(TempGitRepository.InitialContent, await rig.ReadFileAtAsync(expectedPath, "HEAD", TempGitRepository.InitialFile));
		Assert.Equal(await origin.GetHashAsync("main"), await origin.Git.RevParseAsync(expectedPath, "HEAD", Token));
		Repository? stored = await rig.GetStoredAsync();
		Assert.Equal((connected.Id, connected.Path, connected.Origin), (stored?.Id, stored?.Path, stored?.Origin));
	}

	[Fact]
	public async Task CloneAsync_FromALocalRepositoryPath_LeavesTheSourcesExcludeFileAlone()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(origin);
		string exclude = Path.Combine(origin.Folder, ".git", "info", "exclude");
		await using RepositoryRig rig = new();

		ResultAssert.Succeeded(await rig.Setup.CloneAsync(origin.Folder, Token));

		string[] excluded = File.Exists(exclude) ? await File.ReadAllLinesAsync(exclude, Token) : [];
		Assert.DoesNotContain("/.pyxie/", excluded);
		await before.AssertUnchangedAsync(origin);
	}

	[Fact]
	public async Task CloneAsync_TakesTheBranchTheSourceHasCheckedOutAsTheTargetBranch()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await origin.RunGitAsync("checkout", "--quiet", "-b", "develop");
		await origin.CommitFileAsync("develop-only.txt", "only on develop");
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.CloneAsync(origin.Folder, Token));

		Assert.Equal("develop", connected.TargetBranch);
		Assert.Equal("only on develop", await rig.ReadFileAtAsync(connected.Path, "HEAD", "develop-only.txt"));
	}

	[Fact]
	public async Task CloneAsync_ForASourceThatCannotBeCloned_FailsWithGitsReasonAndLeavesNoFolderBehind()
	{
		await using RepositoryRig rig = new();
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-plain");
		string source = scratch.Combine("no-such-repository");

		string error = ResultAssert.Failed(await rig.Setup.CloneAsync(source, Token));

		Assert.StartsWith($"git could not clone {source}: ", error, StringComparison.Ordinal);
		Assert.True(error.Length > $"git could not clone {source}: ".Length, "The message carries git's reason.");
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_ForASourceWhoseHeadIsDetachedAtACommitNoBranchHas_IsRefusedAndLeavesNoFolderBehind()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();

		// A detached HEAD that a branch also points at is cloned onto that branch; one that no branch has leaves the clone detached.
		await origin.RunGitAsync("checkout", "--quiet", "--detach");
		await origin.CommitFileAsync("loose.txt", "a commit on no branch");
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.CloneAsync(origin.Folder, Token));

		Assert.Equal($"No branch is checked out in {origin.Folder}. Check out the branch finished cards should be merged into, then import it.", error);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_ForAnEmptySourceRepository_IsRefusedBecauseItHasNoCommitsAndLeavesNoFolderBehind()
	{
		await using EmptyGitRepository origin = await EmptyGitRepository.CreateAsync();
		await using RepositoryRig rig = new();

		string error = ResultAssert.Failed(await rig.Setup.CloneAsync(origin.Folder, Token));

		Assert.EndsWith($" in {origin.Folder} has no commits yet. Make a first commit, then import it.", error, StringComparison.Ordinal);
		Assert.Null(await rig.GetStoredAsync());
		await rig.AssertNothingImportedAsync();
	}

	[Fact]
	public async Task CloneAsync_WhileAProjectIsOpen_IsRefusedWithoutCloningAnything()
	{
		await using TempGitRepository connectedRepository = await TempGitRepository.CreateAsync();
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(connectedRepository.Folder, Token));

		string error = ResultAssert.Failed(await rig.Setup.CloneAsync(origin.Folder, Token));

		Assert.Equal("A project is open. Close it to import another.", error);
		Repository? stored = await rig.GetStoredAsync();
		Assert.Equal(RepositorySource.Folder, stored?.Source);
		Assert.Equal([rig.ProjectFolderFor(connectedRepository.Folder)], Directory.GetDirectories(rig.Layout.ProjectsFolder));
	}

	[Fact]
	public async Task CloneAsync_WhileAProjectIsOpen_RefusesBeforeTryingToCloneAnything()
	{
		await using TempGitRepository connectedRepository = await TempGitRepository.CreateAsync();
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-plain");
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(connectedRepository.Folder, Token));

		// A clone that was attempted would fail for this source and say so; the open project is reported instead because no clone is started.
		string error = ResultAssert.Failed(await rig.Setup.CloneAsync(scratch.Combine("no-such-repository"), Token));

		Assert.Equal("A project is open. Close it to import another.", error);
	}

	[Fact]
	public async Task CloseAsync_AfterAClone_ClosesTheProjectButLeavesTheHubOnDisk()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.CloneAsync(origin.Folder, Token));
		Project project = rig.OpenProject.Current ?? throw new InvalidOperationException("The clone did not open its project.");

		ResultAssert.Succeeded(await rig.Sessions.CloseAsync(project.Id, Token));

		Assert.Null(await rig.GetStoredAsync());
		Assert.True(Directory.Exists(connected.Path), "Closing leaves the hub where it is.");
	}
}
