using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

public sealed class CardIntegratorLockTests
{
	private const string MergedIntoTheCard = "main had moved on, so it was merged into pyxie/card-1; the gates check the combination.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	// A free lock is granted at once, so a request that is still waiting while the integrator is held inside its move of main shows that the integrator holds the lock there.
	[Fact]
	public async Task IntegrateAsync_HoldsTheRepositoryLockWhileItMovesTheTargetBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		Task<IDisposable> control = rig.RepositoryLock.AcquireAsync(Token);
		bool grantedAtOnceWhenFree = control.IsCompleted;
		(await control).Dispose();
		await using BranchUpdateHook hold = await BranchUpdateHook.HoldAsync(rig.Hub, "main");

		Task<RoundVerdict> integrate = rig.IntegrateAsync(1);
		await hold.WaitUntilHeldAsync();
		Task<IDisposable> request = rig.RepositoryLock.AcquireAsync(Token);
		bool grantedWhileHeld = request.IsCompleted;
		hold.Release();
		RoundVerdict verdict = await integrate;
		using IDisposable granted = await request;

		Assert.True(grantedAtOnceWhenFree, "A free repository lock must be granted at once, or the check below proves nothing.");
		Assert.False(grantedWhileHeld, "The integrator must hold the repository lock while it moves the target branch.");
		Assert.Equal(RoundResult.Succeeded, verdict.Result);
	}

	[Fact]
	public async Task IntegrateAsync_WaitingForTheRepositoryLock_ReadsTheBranchesAgainOnceItHasTheLock()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		Task<RoundVerdict> integrate;
		string movedOn;

		using (await rig.RepositoryLock.AcquireAsync(Token))
		{
			integrate = rig.IntegrateAsync(1);
			movedOn = await rig.PushToTargetAsync("main-moved-on.txt", "main moved on");
		}

		RoundVerdict verdict = await integrate;

		Assert.Equal(RoundResult.Outdated, verdict.Result);
		Assert.Equal(MergedIntoTheCard, verdict.Note);
		Assert.Equal(movedOn, await rig.TipAsync("main"));
		Assert.Equal($"{cardTip} {movedOn}", await rig.ParentsAsync("pyxie/card-1"));
	}

	[Fact]
	public async Task IntegrateAsync_ForTwoCardsAtOnce_LandsOneAndMergesTheNewTargetIntoTheOther()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string firstTip = await rig.CommitOnCardAsync(1, "first.txt", "the first card's work");
		string secondTip = await rig.CommitOnCardAsync(2, "second.txt", "the second card's work");
		string mainTip = await rig.TipAsync("main");

		RoundVerdict[] verdicts = await Task.WhenAll(rig.IntegrateAsync(1), rig.IntegrateAsync(2));

		Assert.Single(verdicts, verdict => verdict.Result == RoundResult.Succeeded);
		Assert.Single(verdicts, verdict => verdict.Result == RoundResult.Outdated);
		int landed = verdicts[0].Result == RoundResult.Succeeded ? 1 : 2;
		int waited = 3 - landed;
		string landedTip = landed == 1 ? firstTip : secondTip;
		string waitedTip = landed == 1 ? secondTip : firstTip;
		string mergeCommit = await rig.TipAsync("main");
		Assert.Equal($"{mainTip} {landedTip}", await rig.ParentsAsync(mergeCommit));
		Assert.Equal($"main had moved on, so it was merged into pyxie/card-{waited}; the gates check the combination.", verdicts[waited - 1].Note);
		Assert.Equal($"{waitedTip} {mergeCommit}", await rig.ParentsAsync($"pyxie/card-{waited}"));
	}
}
