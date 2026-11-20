using Pyxie.Runtime.Projects;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>A refused database migration undoes the import, leaves the source alone, and releases the project change for a retry.</summary>
public sealed class ProjectSetupServiceDatabaseFailureTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ImportFolderAsync_WhenTheDatabaseCannotBeMigrated_ReturnsTheRefusalUndoesTheImportAndCanBeRetried(bool fromAnotherBuild)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(source);
		await using RepositoryRig rig = new();
		UnopenableProjectDatabase database = new(fromAnotherBuild);
		ProjectSetupService setup = new(rig.Catalog, database, rig.Repositories, rig.OpenProject, rig.Git, rig.Layout, rig.Controller, rig.Activity);
		int announcements = 0;
		rig.Activity.ProjectChanged += () => announcements++;

		string error = ResultAssert.Failed(await setup.ImportFolderAsync(source.Folder, token));

		Assert.Contains(fromAnotherBuild ? "different build of Pyxie" : "damaged or is not a SQLite database", error, StringComparison.Ordinal);
		await rig.AssertNothingImportedAsync();
		Assert.Equal(0, announcements);
		await before.AssertUnchangedAsync(source);

		database.FailMigration = false;
		ResultAssert.Succeeded(await setup.ImportFolderAsync(source.Folder, token));
		Assert.Equal(rig.ProjectFolderFor(source.Folder), rig.OpenProject.Current?.Folder);
		Assert.Equal(1, announcements);
	}
}
