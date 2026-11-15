using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>An imported folder shares no files with the project's hub. A local clone would hardlink the source's objects, and removing the project folder after a refused import clears read-only flags on every file it holds, which would reach through the links into the source's own <c>.git</c>.</summary>
public sealed class ProjectSetupServiceSourceIsolationTests
{
	[Fact]
	public async Task ImportFolderAsync_WhenTheConnectionIsRefusedAfterTheClone_LeavesTheAttributesOfTheSourcesObjectFilesAlone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new(repositories: new RefusingRepositoryService());
		string objectsFolder = Path.Combine(source.Folder, ".git", "objects");
		string[] objects = [.. Directory.GetFiles(objectsFolder, "*", SearchOption.AllDirectories)];
		Dictionary<string, FileAttributes> before = objects.ToDictionary(file => file, File.GetAttributes);
		Assert.True(objects.Length > 0, "The source repository should have object files to compare.");

		ResultAssert.Failed(await rig.Setup.ImportFolderAsync(source.Folder, TestContext.Current.CancellationToken));

		string changed = string.Join("; ", objects.Where(file => File.GetAttributes(file) != before[file]).Select(file => $"{Path.GetRelativePath(objectsFolder, file)}: {before[file]} -> {File.GetAttributes(file)}"));
		Assert.True(changed.Length == 0, $"Removing the refused clone changed object files in the source: {changed}");
	}
}
