using Pyxie.Data.Entities;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>A repository whose branch shares its name with a tag imports like any other: the target branch is the plain branch name, and the branch list shows it.</summary>
public sealed class ProjectSetupServiceTagNameTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ImportFolderAsync_WhenATagHasTheCheckedOutBranchsName_ConnectsWithThePlainBranchAsTheTarget()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.RunGitAsync("tag", "main");
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		Assert.Equal("main", connected.TargetBranch);
		Assert.Equal("main", (await rig.GetStoredAsync())?.TargetBranch);
	}

	[Fact]
	public async Task GetOverviewAsync_ForAnImportedRepositoryWhoseTagShareTheBranchName_ListsThePlainBranchName()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.RunGitAsync("tag", "main");
		await using RepositoryRig rig = new();
		ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, Token));

		RepositoryOverview? overview = await rig.Browser.GetOverviewAsync(Token);

		Assert.NotNull(overview);
		Assert.Equal("main", Assert.Single(overview.Branches).Name);
	}
}
