using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A resolve round's sandbox is a fresh one on the card's last commit, in the middle of the merge Pyxie started: the target branch's commit that was current when the sandbox opened is merged into the card's branch without committing, so the merge agent finds the conflicts waiting. It has no volumes and nothing of an earlier round.</summary>
public sealed class CardSandboxesResolveTests
{
	private static readonly AgentSnapshot s_merger = new("Merger", HarnessKind.Other, "merger", "--resolve", AgentAuthentication.None);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OpenAsync_ForAResolveRound_StartsTheMergeOfTheTargetBranchAndLeavesItsConflictsToResolve()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, string targetTip) = await rig.ConflictAsync();

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal((cardTip, targetTip), (card.BaseCommit, card.TargetCommit));
		Assert.Equal("pyxie/card-1", card.Branch);
		Assert.False(card.BranchIsNew);
		Assert.Equal(rig.SandboxFolder(1, SandboxRole.Resolve), card.Folder);
		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(repository));
		Assert.Equal(cardTip, await rig.HashInAsync(repository));
		Assert.True(await rig.IsMergingAsync(repository), "The merge must be started and left uncommitted.");
		Assert.Equal(targetTip, await rig.HashInAsync(repository, "MERGE_HEAD"));
		Assert.Equal([$"UU {TempGitRepository.InitialFile}"], await WorkingTreeStatus.ListAsync(repository));
		string conflicted = await CardSandboxRig.ReadAsync(repository, TempGitRepository.InitialFile) ?? string.Empty;
		Assert.Contains("<<<<<<<", conflicted, StringComparison.Ordinal);
		Assert.Contains("the card's version", conflicted, StringComparison.Ordinal);
		Assert.Contains("the target's version", conflicted, StringComparison.Ordinal);
		Assert.Equal(targetTip, await rig.HashInAsync(repository, "main"));
		Assert.StartsWith("Merge main into pyxie/card-1", await CardSandboxRig.ReadAsync(repository, ".git/MERGE_MSG"), StringComparison.Ordinal);
	}

	[Fact]
	public async Task OpenAsync_ForAResolveRound_StartsAFreshSandboxWithNoVolumesThatIsNotKept()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.ConflictAsync();

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);

		SandboxSpec spec = rig.Sandboxes.Started[^1];
		Assert.Equal(SandboxRole.Resolve, spec.Role);
		Assert.Equal(SandboxNames.Container(rig.ProjectKey, 1, SandboxRole.Resolve), spec.Name);
		Assert.Empty(spec.Volumes);
		Assert.False(spec.Keep, "A resolve sandbox is fresh for each round.");
		Assert.Equal(["refs/heads/main", "refs/heads/pyxie/card-1"], await rig.HandInRefsAsync(1, SandboxRole.Resolve));
	}

	[Fact]
	public async Task OpenAsync_ForAResolveRoundWhenTheTargetMovedAfterItWasRead_MergesTheCommitItReadAndNothingNewer()
	{
		MovingSignIns signIns = new();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, signIns: signIns);
		(_, string pinned) = await rig.ConflictAsync();
		string newer = string.Empty;
		signIns.Action = async () => newer = await rig.MoveTargetAsync("pushed-meanwhile.txt", "pushed while the sandbox was being made");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);

		string repository = CardSandboxRig.HostPath(card);
		Assert.NotEqual(string.Empty, newer);
		Assert.NotEqual(pinned, newer);
		Assert.Equal(newer, await rig.Hub.GetHashAsync("main"));
		Assert.Equal(pinned, card.TargetCommit);
		Assert.Equal(pinned, await rig.HashInAsync(repository, "MERGE_HEAD"));
		Assert.Equal(newer, await rig.HashInAsync(repository, "refs/remotes/origin/main"));
		Assert.False(File.Exists(Path.Combine(repository, "pushed-meanwhile.txt")), "What was pushed after the target's tip was read must not be in the merge.");
	}

	[Fact]
	public async Task OpenAsync_ForAResolveRoundThatMergesCleanly_LeavesTheMergeUncommittedForTheAgentToFinish()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string cardTip = await rig.WorkRoundAsync(1, 1, "work.txt", "the card's file");
		string targetTip = await rig.MoveTargetAsync("moved-on.txt", "the target's file");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(cardTip, await rig.HashInAsync(repository));
		Assert.True(await rig.IsMergingAsync(repository));
		Assert.Equal(targetTip, await rig.HashInAsync(repository, "MERGE_HEAD"));
		Assert.Equal(["A  moved-on.txt"], await WorkingTreeStatus.ListAsync(repository));
	}

	[Fact]
	public async Task OpenAsync_ForAResolveRoundWhenTheCardAlreadyHasTheTargetBranch_ThrowsASandboxExceptionAndLetsTheSandboxGo()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "work.txt", "the card's file");

		SandboxException failure = await Assert.ThrowsAsync<SandboxException>(() => rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger));

		Assert.StartsWith("git merge ", failure.Message, StringComparison.Ordinal);
		Assert.Contains("did not start a merge in the sandbox", failure.Message, StringComparison.Ordinal);
		Assert.False(Directory.Exists(rig.Sandboxes.WorkFolder(SandboxNames.Container(rig.ProjectKey, 1, SandboxRole.Resolve))), "A sandbox that was never handed out must be removed.");
	}

	[Fact]
	public async Task OpenAsync_ForALaterResolveRound_StartsTheMergeAgainWithNothingTheEarlierOneLeft()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, string targetTip) = await rig.ConflictAsync();
		await using (CardSandbox earlier = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger))
		{
			string repository = CardSandboxRig.HostPath(earlier);
			await CardSandboxRig.WriteAsync(repository, "left-behind.txt", "what the earlier round left");
			await CardSandboxRig.WriteAsync(repository, TempGitRepository.InitialFile, "half resolved");
			await rig.RunGitInAsync(repository, "add", "--all");
		}

		await using CardSandbox card = await rig.OpenAsync(1, 3, RoundKind.Resolve, s_merger);

		string fresh = CardSandboxRig.HostPath(card);
		Assert.False(File.Exists(Path.Combine(fresh, "left-behind.txt")), "A resolve sandbox keeps nothing of an earlier round.");
		Assert.Equal(cardTip, await rig.HashInAsync(fresh));
		Assert.Equal(targetTip, await rig.HashInAsync(fresh, "MERGE_HEAD"));
		Assert.Contains("<<<<<<<", await CardSandboxRig.ReadAsync(fresh, TempGitRepository.InitialFile), StringComparison.Ordinal);
	}

	[Fact]
	public async Task OpenAsync_ForAResolveRound_LeavesTheSteeringFilesAsTheCardHasThemForTheMergeAgent()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string cardTip = await rig.WorkRoundAsync(1, 1, "CLAUDE.md", "the card's instructions");
		await rig.MoveTargetAsync("CLAUDE.md", "the target's instructions");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(cardTip, await rig.HashInAsync(repository));
		Assert.Contains("the card's instructions", await CardSandboxRig.ReadAsync(repository, "CLAUDE.md"), StringComparison.Ordinal);
		Assert.Contains("<<<<<<<", await CardSandboxRig.ReadAsync(repository, "CLAUDE.md"), StringComparison.Ordinal);
	}
}
