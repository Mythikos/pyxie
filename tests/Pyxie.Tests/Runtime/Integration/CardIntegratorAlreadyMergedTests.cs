using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

public sealed class CardIntegratorAlreadyMergedTests
{
	private const string AlreadyMerged = "Already merged into main.";

	[Fact]
	public async Task IntegrateAsync_ForACardThatMadeNoChanges_SucceedsWithoutChangingTheHub()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.StartEmptyCardAsync(1);
		IReadOnlyList<string> refsBefore = await rig.ListRefsAsync();

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(AlreadyMerged, verdict.Note);
		Assert.Null(verdict.Report);
		Assert.Equal(refsBefore, await rig.ListRefsAsync());
	}

	[Fact]
	public async Task IntegrateAsync_ForACardWhoseWorkAPersonAlreadyMerged_SucceedsWithoutAnotherMergeCommit()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		await rig.UpdatePersonAsync();
		await rig.Person.RunGitAsync("merge", "--quiet", "--no-ff", "--no-edit", "origin/pyxie/card-1");
		await rig.Person.PushAsync("main");
		string mainTip = await rig.TipAsync("main");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(AlreadyMerged, verdict.Note);
		Assert.Equal(mainTip, await rig.TipAsync("main"));
	}

	[Fact]
	public async Task IntegrateAsync_ForACardWithNoChangesWhileTheTargetMovedOn_IsStillAlreadyMerged()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.StartEmptyCardAsync(1);
		string mainTip = await rig.PushToTargetAsync("moved-on.txt", "main moved on");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(AlreadyMerged, verdict.Note);
		Assert.Equal(mainTip, await rig.TipAsync("main"));
		Assert.Equal(cardTip, await rig.TipAsync("pyxie/card-1"));
	}

	[Fact]
	public async Task IntegrateAsync_CalledAgainAfterTheMergeLanded_SucceedsWithoutAnotherMergeCommit()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		RoundVerdict first = await rig.IntegrateAsync(1);
		string mergeCommit = await rig.TipAsync("main");

		RoundVerdict second = await rig.IntegrateAsync(1, sequence: 5);

		Assert.Equal(RoundResult.Succeeded, first.Result);
		Assert.NotEqual(AlreadyMerged, first.Note);
		Assert.Equal(RoundResult.Succeeded, second.Result);
		Assert.Equal(AlreadyMerged, second.Note);
		Assert.Equal(mergeCommit, await rig.TipAsync("main"));
	}
}
