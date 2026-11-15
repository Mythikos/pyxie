using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

public sealed class CardIntegratorMissingBranchTests
{
	[Fact]
	public async Task IntegrateAsync_WhenTheCardBranchIsNotInTheHub_ThrowsAGitExceptionThatSaysToEditTheCard()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		IReadOnlyList<string> refsBefore = await rig.ListRefsAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => rig.IntegrateAsync(1));

		Assert.Equal("The card's branch pyxie/card-1 is not in the repository, so there is nothing to merge. Edit the card so it starts again at work.", failure.Message);
		Assert.Equal(refsBefore, await rig.ListRefsAsync());
	}

	// The hub refuses a push that deletes the target branch, so only a change made in the hub itself can take it away.
	[Fact]
	public async Task IntegrateAsync_WhenTheTargetBranchIsGone_ThrowsAGitException()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		await rig.Hub.RunGitAsync("update-ref", "-d", "refs/heads/main");

		await Assert.ThrowsAsync<GitException>(() => rig.IntegrateAsync(1));

		Assert.Null(await rig.Hub.Git.ResolveCommitAsync(rig.Hub.Folder, "refs/heads/main", TestContext.Current.CancellationToken));
		Assert.Equal(cardTip, await rig.TipAsync("pyxie/card-1"));
	}
}
