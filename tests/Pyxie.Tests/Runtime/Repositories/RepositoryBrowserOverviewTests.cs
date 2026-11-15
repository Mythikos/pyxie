using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

public sealed class RepositoryBrowserOverviewTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetOverviewAsync_WhenNoRepositoryIsConnected_IsNull()
	{
		await using RepositoryRig rig = new();

		Assert.Null(await rig.Browser.GetOverviewAsync(Token));
	}

	[Fact]
	public async Task GetOverviewAsync_ForAFolderImport_NamesItAfterItsFolder()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = await rig.ConnectAsync(repository.Folder);

		RepositoryOverview? overview = await rig.Browser.GetOverviewAsync(Token);

		Assert.NotNull(overview);
		Assert.Equal(Path.GetFileName(repository.Folder), overview.Name);
		Assert.Equal(connected.Id, overview.Repository.Id);
	}

	[Fact]
	public async Task GetOverviewAsync_ForAFolderImport_NamesItAfterTheFolderItCameFromNotTheClone()
	{
		await using TempGitRepository clone = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		await rig.ConnectAsync(clone.Folder, origin: RepositoryTestInput.PathFor("my-app"));

		RepositoryOverview? overview = await rig.Browser.GetOverviewAsync(Token);

		Assert.Equal("my-app", overview?.Name);
	}

	[Fact]
	public async Task GetOverviewAsync_ForAFolderStoredWithATrailingSeparator_StillNamesItAfterTheFolder()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		await rig.ConnectAsync(repository.Folder + Path.DirectorySeparatorChar);

		RepositoryOverview? overview = await rig.Browser.GetOverviewAsync(Token);

		Assert.Equal(Path.GetFileName(repository.Folder), overview?.Name);
	}

	[Theory]
	[InlineData("https://github.com/team/app.git", "app")]
	[InlineData("https://github.com/team/app", "app")]
	[InlineData("https://github.com/team/app/", "app")]
	[InlineData("git@github.com:team/app.git", "app")]
	[InlineData("git@github.com:app.git", "app")]
	[InlineData("ssh://git@host:2222/team/svc.git", "svc")]
	[InlineData("https://host/team/Tool.GIT", "Tool")]
	[InlineData("C:\\src\\repos\\tool.git", "tool")]
	[InlineData("C:\\src\\repos\\tool\\", "tool")]
	[InlineData("app.git", "app")]
	public async Task GetOverviewAsync_ForAUrlImport_NamesItAfterTheLastPartOfTheUrl(string url, string expectedName)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		await rig.ConnectAsync(repository.Folder, RepositorySource.Url, url);

		RepositoryOverview? overview = await rig.Browser.GetOverviewAsync(Token);

		Assert.Equal(expectedName, overview?.Name);
	}

	[Fact]
	public async Task GetOverviewAsync_ListsTheBranchesMostRecentlyCommittedFirst()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CreateBranchAsync("old");
		await repository.CheckoutAsync("old");
		await DatedCommits.CommitFileAtAsync(repository, "old.txt", "old", new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), "Old work");
		await repository.CheckoutAsync("main");
		await repository.CreateBranchAsync("new");
		await repository.CheckoutAsync("new");
		await DatedCommits.CommitFileAtAsync(repository, "new.txt", "new", new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.Zero), "New work");
		await repository.CheckoutAsync("main");
		await using RepositoryRig rig = new();
		await rig.ConnectAsync(repository.Folder);

		RepositoryOverview? overview = await rig.Browser.GetOverviewAsync(Token);

		Assert.NotNull(overview);
		Assert.Equal(["new", "main", "old"], overview.Branches.Select(branch => branch.Name));
		Assert.Equal("New work", overview.Branches[0].Subject);
	}

	[Fact]
	public async Task GetOverviewAsync_WhenTheRepositoryFolderIsGone_ThrowsAGitException()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-gone");
		await using RepositoryRig rig = new();
		await rig.ConnectAsync(scratch.Combine("moved-away"));

		await Assert.ThrowsAsync<GitException>(() => rig.Browser.GetOverviewAsync(Token));
	}
}
