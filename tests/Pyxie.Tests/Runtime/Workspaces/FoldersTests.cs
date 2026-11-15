using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

public sealed class FoldersTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Delete_RemovesAFolderWithNestedAndReadOnlyFiles()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-folders");
		string folder = scratch.Combine("target");
		string nested = Path.Combine(folder, "objects", "ab");
		Directory.CreateDirectory(nested);
		string readOnly = Path.Combine(nested, "object");
		await File.WriteAllTextAsync(readOnly, "git marks its object files read-only", Token);
		File.SetAttributes(readOnly, FileAttributes.ReadOnly);
		await File.WriteAllTextAsync(Path.Combine(folder, "plain.txt"), "plain", Token);

		Folders.Delete(folder);

		Assert.False(Directory.Exists(folder), "The folder and everything in it must be gone.");
		Assert.True(Directory.Exists(scratch.Folder), "Only the folder is removed, not its parent.");
	}

	[Fact]
	public async Task Delete_ForAFolderThatDoesNotExist_DoesNothing()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-folders");

		Folders.Delete(scratch.Combine("missing"));

		Assert.True(Directory.Exists(scratch.Folder), "Deleting a missing folder must not touch anything else.");
	}
}
