using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

public sealed class CardIntegratorCompareAndSwapTests
{
	private const string MergedIntoTheCard = "main had moved on, so it was merged into pyxie/card-1; the gates check the combination.";

	[Fact]
	public async Task IntegrateAsync_WhenTheTargetMovesJustBeforeTheMergeLands_IsOutdatedAndTheNextRoundBringsTheTargetIn()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");

		// A person's commit on top of main, already in the hub on a branch of their own, which the race then makes main.
		await rig.UpdatePersonAsync();
		string personsCommit = await rig.Person.CommitFileAsync("persons-work.txt", "a person's work");
		await rig.Person.PushAsync("HEAD:refs/heads/person/next");
		await using BranchUpdateHook race = await BranchUpdateHook.RaceAsync(rig.Hub, "main", personsCommit);

		RoundVerdict verdict = await rig.IntegrateAsync(1);
		bool raced = race.IsSpent;
		string mainAfterTheRace = await rig.TipAsync("main");
		string cardAfterTheRace = await rig.TipAsync("pyxie/card-1");
		RoundVerdict next = await rig.IntegrateAsync(1, sequence: 7);

		Assert.True(raced, "The hook must have stepped into the integrator's move of main.");
		Assert.Equal(RoundResult.Outdated, verdict.Result);
		Assert.Equal("main moved on while the merge was being written; the next merge round brings it in.", verdict.Note);
		Assert.Equal(personsCommit, mainAfterTheRace);
		Assert.Equal(cardTip, cardAfterTheRace);
		Assert.Equal(RoundResult.Outdated, next.Result);
		Assert.Equal(MergedIntoTheCard, next.Note);
		Assert.Equal($"{cardTip} {personsCommit}", await rig.ParentsAsync("pyxie/card-1"));
		Assert.Equal(personsCommit, await rig.TipAsync("main"));
	}

	[Fact]
	public async Task IntegrateAsync_WhenTheCardBranchMovesWhileTheTargetIsMergedIntoIt_IsOutdatedAndKeepsTheNewWork()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		string mainTip = await rig.PushToTargetAsync("main-moved-on.txt", "main moved on");

		// The worker's next commit, already in the hub under a ref of the test's own, which the race then makes the card branch.
		await rig.PrepareWorkerAsync(1);
		string workersCommit = await rig.Worker.CommitFileAsync("more.txt", "more of the card's work");
		await rig.HandBackAsync(1, "refs/tests/next");
		await using BranchUpdateHook race = await BranchUpdateHook.RaceAsync(rig.Hub, "pyxie/card-1", workersCommit);

		RoundVerdict verdict = await rig.IntegrateAsync(1);
		bool raced = race.IsSpent;
		string cardAfterTheRace = await rig.TipAsync("pyxie/card-1");
		RoundVerdict next = await rig.IntegrateAsync(1, sequence: 5);

		Assert.True(raced, "The hook must have stepped into the integrator's move of the card branch.");
		Assert.Equal(RoundResult.Outdated, verdict.Result);
		Assert.Equal("pyxie/card-1 moved while main was being merged into it; the next merge round tries again.", verdict.Note);
		Assert.Equal(workersCommit, cardAfterTheRace);
		Assert.Equal(RoundResult.Outdated, next.Result);
		Assert.Equal(MergedIntoTheCard, next.Note);
		Assert.Equal($"{workersCommit} {mainTip}", await rig.ParentsAsync("pyxie/card-1"));
		Assert.Equal(mainTip, await rig.TipAsync("main"));
	}
}
