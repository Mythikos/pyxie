using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientRemoteTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CloneBareAsync_ClonesIntoANewFolderCreatingItsParentAsABareRepository()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-clone");
		string destination = scratch.Combine("not", "yet", "there", "clone");

		ProcessResult result = await origin.Git.CloneBareAsync(origin.Folder, destination, Token);

		Assert.True(result.Succeeded, $"The clone should succeed: {result.Output}");
		Assert.Equal("true", await origin.RunGitAsync("-C", destination, "rev-parse", "--is-bare-repository"));
		Assert.False(Directory.Exists(Path.Combine(destination, ".git")), "A bare clone keeps git's files in the folder itself.");
		Assert.True(File.Exists(Path.Combine(destination, "HEAD")), "A bare clone holds HEAD in the folder itself.");
		Assert.Equal(TempGitRepository.InitialContent, (await origin.Git.ReadFileAsync(destination, "HEAD", TempGitRepository.InitialFile, 1000, Token))?.Text);
		Assert.Equal("main", await origin.Git.GetCurrentBranchAsync(destination, Token));
		Assert.Equal(await origin.GetHashAsync(), await origin.Git.RevParseAsync(destination, "HEAD", Token));
	}

	[Fact]
	public async Task CloneBareAsync_ForASourceThatDoesNotExist_FailsWithGitsReason()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-clone");

		ProcessResult result = await origin.Git.CloneBareAsync(scratch.Combine("no-such-repository"), scratch.Combine("clone"), Token);

		Assert.False(result.Succeeded, "Cloning a path that is not a repository must fail.");
		Assert.Contains("fatal", result.Output, StringComparison.OrdinalIgnoreCase);
	}
}
