using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A merge round goes to the integrator, which merges in the hub without a sandbox. A lock on the target branch's ref is not a lost race: the round is blocked with git's reason, instead of coming back outdated, going through the gates again, and trying the same move forever without using an attempt.</summary>
public sealed class RoundExecutorMergeTests
{
	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAMergeRound_MergesTheCardIntoTheTargetInTheHubWithoutStartingASandbox()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string initialMain = await rig.Hub.GetHashAsync("main");
		string cardTip = await rig.WorkAsync();
		int sandboxesBefore = rig.StartedSandboxes().Count;

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Merge);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.StartsWith("Merged into main as ", verdict.Note, StringComparison.Ordinal);
		Assert.Equal("hello", await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal($"{initialMain} {cardTip}", await rig.Hub.RunGitAsync("log", "-1", "--format=%P", "main"));
		Assert.Equal(sandboxesBefore, rig.StartedSandboxes().Count);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAMergeRoundWhenTheTargetBranchsRefIsLocked_IsBlockedNotOutdatedAndMergesOnceTheLockIsGone()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.WorkAsync();
		string lockFile = Path.Combine(rig.Hub.GitFolder, "refs", "heads", "main.lock");
		await File.WriteAllTextAsync(lockFile, string.Empty, TestContext.Current.CancellationToken);
		string mainTip = await rig.Hub.GetHashAsync("main");

		(RoundVerdict locked, _) = await rig.RunAsync(RoundKind.Merge);

		Assert.Equal(RoundResult.Blocked, locked.Result);
		Assert.StartsWith("git failed: ", locked.Note, StringComparison.Ordinal);
		Assert.Contains("update-ref", locked.Report, StringComparison.Ordinal);
		Assert.Contains("main.lock", locked.Report, StringComparison.Ordinal);
		Assert.Equal(mainTip, await rig.Hub.GetHashAsync("main"));

		File.Delete(lockFile);
		(RoundVerdict merged, _) = await rig.RunAsync(RoundKind.Merge);

		Assert.Equal(RoundResult.Succeeded, merged.Result);
		Assert.Equal("hello", await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
	}
}
