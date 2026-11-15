using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>What a claim takes: interrupted cards before the Queue, and from the Queue the top card that is Ready, never one the pipeline stopped. A claim needs a connected repository, starts attempt 1 of a new run, and gives the agent a briefing that names the branches from the repository and the card.</summary>
public sealed class CardPipelineServiceQueueClaimTests
{
	[Fact]
	public async Task ClaimNextAsync_WithAStoppedCardAtTheHeadOfTheQueue_ClaimsTheReadyCardBehindItAndLeavesTheStoppedCardUntouched()
	{
		await using PipelineRig rig = new();
		WorkCard stopped = await TestData.AddCardAsync(rig.Database, "Stopped", state: CardState.Error, attempt: 2, resumeRound: RoundKind.Review);
		WorkCard ready = await TestData.AddCardAsync(rig.Database, "Ready");

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(ready.Id, work.CardId);
		Assert.Equal(RoundKind.Work, work.Kind);
		await CardAssert.UnchangedAsync(rig.Database, stopped, expectedCardCount: 2);
		Assert.Empty(await TestData.ReadRoundsAsync(rig.Database, stopped.Id));
		Assert.Equal("Stopped", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
	}

	[Fact]
	public async Task ClaimNextAsync_WithStoppedCardsBetweenReadyOnes_TakesTheReadyCardsInQueueOrderAndThenFindsNothing()
	{
		await using PipelineRig rig = new();
		WorkCard firstStopped = await TestData.AddCardAsync(rig.Database, "Stopped 1", state: CardState.Error);
		WorkCard first = await TestData.AddCardAsync(rig.Database, "First");
		WorkCard secondStopped = await TestData.AddCardAsync(rig.Database, "Stopped 2", state: CardState.Error);
		WorkCard second = await TestData.AddCardAsync(rig.Database, "Second");

		PipelineWork claimedFirst = await rig.ClaimAsync();
		PipelineWork claimedSecond = await rig.ClaimAsync();

		Assert.Equal(first.Id, claimedFirst.CardId);
		Assert.Equal(second.Id, claimedSecond.CardId);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		Assert.Equal("Stopped 1,Stopped 2", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
		foreach (WorkCard stopped in new[] { firstStopped, secondStopped })
		{
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, stopped.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardState.Error, stored.State);
			Assert.Equal(stopped.Version, stored.Version);
			Assert.Empty(await TestData.ReadRoundsAsync(rig.Database, stopped.Id));
		}
	}

	[Fact]
	public async Task ClaimNextAsync_WithOnlyStoppedCardsInTheQueue_ReturnsNullAndChangesNothing()
	{
		await using PipelineRig rig = new();
		WorkCard first = await TestData.AddCardAsync(rig.Database, "Stopped 1", state: CardState.Error, attempt: 3, resumeRound: RoundKind.Gate);
		WorkCard second = await TestData.AddCardAsync(rig.Database, "Stopped 2", state: CardState.Error, attempt: 1);

		PipelineWork? work = await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);

		Assert.Null(work);
		await CardAssert.UnchangedAsync(rig.Database, first, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(rig.Database, second, expectedCardCount: 2);
		Assert.Equal(0, await TestData.CountRoundsAsync(rig.Database));
	}

	[Fact]
	public async Task ClaimNextAsync_WithoutARepositoryRow_ReturnsNullAndChangesNothingEvenForAnInterruptedCard()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await TestData.AddCardAsync(rig.Database, "Interrupted");
		await TestData.AddCardAsync(rig.Database, "Waiting");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(gate.RoundId, "The pipeline was stopped.", TestContext.Current.CancellationToken));
		await rig.RemoveRepositoryAsync();
		WorkCard? interrupted = await TestData.FindCardAsync(rig.Database, gate.CardId);
		Assert.NotNull(interrupted);
		WorkCard waiting = (await TestData.ReadLaneAsync(rig.Database, CardLane.Queue)).Single();
		int roundCount = await TestData.CountRoundsAsync(rig.Database);

		PipelineWork? work = await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);

		Assert.Null(work);
		await CardAssert.UnchangedAsync(rig.Database, interrupted, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(rig.Database, waiting, expectedCardCount: 2);
		Assert.Equal(roundCount, await TestData.CountRoundsAsync(rig.Database));

		TestData.ConnectRepository(rig.Database);
		PipelineWork resumed = await rig.ClaimAsync();
		Assert.Equal(gate.CardId, resumed.CardId);
		Assert.Equal(RoundKind.Gate, resumed.Kind);
	}

	[Fact]
	public async Task ClaimNextAsync_ResumesInterruptedCardsOfLaterLanesFirstAndOnlyThenTakesTheQueue()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard working = await TestData.AddCardAsync(rig.Database, "Working", reviewerAgentId: TestData.DefaultAgent);
		WorkCard gating = await TestData.AddCardAsync(rig.Database, "Gating", reviewerAgentId: TestData.DefaultAgent);
		WorkCard reviewing = await TestData.AddCardAsync(rig.Database, "Reviewing", reviewerAgentId: TestData.DefaultAgent);
		WorkCard waiting = await TestData.AddCardAsync(rig.Database, "Waiting", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork[] running = [await rig.ClaimAtAsync(RoundKind.Work), await rig.ClaimAtAsync(RoundKind.Gate), await rig.ClaimAtAsync(RoundKind.Review)];
		foreach (PipelineWork work in running)
		{
			ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(work.RoundId, "The pipeline was stopped.", TestContext.Current.CancellationToken));
		}

		PipelineWork first = await rig.ClaimAsync();
		PipelineWork second = await rig.ClaimAsync();
		PipelineWork third = await rig.ClaimAsync();
		Assert.Equal("Waiting", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		PipelineWork fourth = await rig.ClaimAsync();

		Assert.Equal([reviewing.Id, gating.Id, working.Id, waiting.Id], new[] { first.CardId, second.CardId, third.CardId, fourth.CardId });
		Assert.Equal([RoundKind.Review, RoundKind.Gate, RoundKind.Work, RoundKind.Work], new[] { first.Kind, second.Kind, third.Kind, fourth.Kind });
		Assert.Equal([1, 1, 1, 1], new[] { first.Attempt, second.Attempt, third.Attempt, fourth.Attempt });
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ClaimNextAsync_OfAQueueCard_StartsAttemptOneAndOpensWorkInInProgress()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", attemptLimit: 4);
		Assert.Equal(0, card.Attempt);

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(card.Id, work.CardId);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(1, work.Attempt);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(1, stored.Attempt);
		Assert.Null(stored.ResumeRound);
		Assert.Equal(card.Version + 1, stored.Version);
		Assert.Equal(["Work@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	public async Task ClaimNextAsync_OpensABriefingThatNamesTheTargetBranchOfTheRepositoryAndTheCardsOwnBranch(RoundKind kind)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await PipelineArrangement.MoveTargetBranchAsync(rig, "release/2.0");
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);

		PipelineWork work = await rig.ClaimAtAsync(kind);

		Assert.NotNull(work.Briefing);
		string workspace = BriefingSections.Get(work.Briefing, "Workspace");
		Assert.Contains($"`{PipelineContract.BranchName(card.Id)}`", workspace, StringComparison.Ordinal);
		Assert.Contains("`release/2.0`", workspace, StringComparison.Ordinal);
		Assert.DoesNotContain("`main`", workspace, StringComparison.Ordinal);
		if (kind == RoundKind.Review)
		{
			Assert.Contains("git diff release/2.0...HEAD", workspace, StringComparison.Ordinal);
		}

		CardRound stored = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(work.RoundId, stored.Id);
		Assert.Equal(work.Briefing, stored.Briefing);
	}

	[Fact]
	public async Task RecordResultAsync_ReadsTheTargetBranchFromTheRepositoryEachTimeItOpensAWorkRound()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await rig.CreateCardAsync("Card");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		await PipelineArrangement.MoveTargetBranchAsync(rig, "develop");

		PipelineWork? work = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

		Assert.NotNull(work);
		Assert.NotNull(work.Briefing);
		string workspace = BriefingSections.Get(work.Briefing, "Workspace");
		Assert.Contains("`develop`", workspace, StringComparison.Ordinal);
		Assert.DoesNotContain("`main`", workspace, StringComparison.Ordinal);
	}
}
