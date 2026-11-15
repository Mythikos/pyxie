using Pyxie.Data.Enums;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A gate, review, or resolve round checks or merges work that is on the card's branch. When the branch is gone (the repository was imported again, or someone deleted it) there is no work to check, so the round fails with a message instead of starting from the target branch and passing a card that has nothing on it. A work round starts the branch again instead.</summary>
public sealed class CardSandboxesMissingBranchTests
{
	[Theory]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public async Task OpenAsync_ForARoundThatChecksWorkWhenTheCardsBranchIsMissing_ThrowsAndStartsNothing(RoundKind kind)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);

		GitException failure = await Assert.ThrowsAsync<GitException>(() => rig.OpenAsync(1, 5, kind));

		Assert.Contains("The card's branch pyxie/card-1 is not in the repository, so there is no work for this ", failure.Message, StringComparison.Ordinal);
		Assert.Contains("Edit the card so it starts again at work.", failure.Message, StringComparison.Ordinal);
		Assert.Null(await rig.HubCardTipAsync(1));
		Assert.Empty(rig.Sandboxes.Started);
		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, 1)), "No host folder is made for a card that has no work.");
	}

	[Theory]
	[InlineData(RoundKind.Gate, SandboxRole.Gates)]
	[InlineData(RoundKind.Review, SandboxRole.Review)]
	[InlineData(RoundKind.Resolve, SandboxRole.Resolve)]
	public async Task OpenAsync_ForARoundThatChecksWorkWhenTheCardsBranchWasDeletedAfterItsWork_ThrowsToo(RoundKind kind, SandboxRole role)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "work.txt", "the worker's commit");
		await rig.Hub.RunGitAsync("update-ref", "-d", "refs/heads/pyxie/card-1");

		GitException failure = await Assert.ThrowsAsync<GitException>(() => rig.OpenAsync(1, 5, kind));

		Assert.Contains("is not in the repository", failure.Message, StringComparison.Ordinal);
		Assert.Null(await rig.HubCardTipAsync(1));
		Assert.DoesNotContain(rig.Sandboxes.Started, spec => spec.Role == role);
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRoundWhenTheCardsBranchIsMissing_StartsItAgainFromTheTargetBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string mainTip = await rig.Hub.GetHashAsync("main");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		Assert.True(card.BranchIsNew, "The hub has no branch for the card, so its work starts a new one.");
		Assert.Equal(mainTip, card.BaseCommit);
		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(CardSandboxRig.HostPath(card)));
		Assert.Equal(mainTip, await rig.HashInAsync(CardSandboxRig.HostPath(card)));
	}
}
