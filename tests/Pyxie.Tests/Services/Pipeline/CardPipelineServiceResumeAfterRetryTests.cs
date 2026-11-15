using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A person retries a card the pipeline stopped, and the next claim resumes it where it stopped on a fresh first attempt. Editing a stopped card clears where it stopped, so the edited task starts again at work.</summary>
public sealed class CardPipelineServiceResumeAfterRetryTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task ClaimNextAsync_AfterABlockedRoundIsRetried_OpensThatStageOnAFreshFirstAttempt(RoundKind kind)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork blocked = await PipelineArrangement.ClaimAtAttemptAsync(rig, kind, attempt: 2);
		await rig.RecordAsync(blocked, RoundResult.Blocked, "Needs a person");
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(card.Id, resumed.CardId);
		Assert.Equal(kind, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
		Assert.Equal(blocked.Sequence + 1, resumed.Sequence);
		Assert.Equal(kind.RunsAgent(), resumed.Agent is not null);
		Assert.Equal(kind.RunsAgent(), resumed.Briefing is not null);
		Assert.Equal([$"{kind}@2:Blocked", $"{kind}@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(PipelineRules.LaneOf(kind), stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(1, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAMergeWasBlockedAndRetried_OpensTheMergeRoundInTheReviewLaneWithoutRunningWorkAgain()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await rig.RecordAsync(merge, RoundResult.Blocked, "The target branch has local changes in the way.");
		WorkCard? stopped = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stopped);
		Assert.Equal(CardLane.Queue, stopped.Lane);
		Assert.Equal(RoundKind.Merge, stopped.ResumeRound);
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Merge, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
		Assert.Equal(5, resumed.Sequence);
		Assert.Null(resumed.Agent);
		Assert.Null(resumed.Briefing);
		Assert.Equal(["Work@1:Completed", "Gate@1:Passed", "Review@1:Approved", "Merge@1:Blocked", "Merge@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);

		PipelineWork? end = await rig.RecordAsync(resumed, RoundResult.Succeeded);
		Assert.Null(end);
		WorkCard? complete = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(complete);
		Assert.Equal(CardLane.Complete, complete.Lane);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAReviewKeptErroringUntilTheLastAttemptAndWasRetried_OpensTheReviewAgainOnAFreshFirstAttempt()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 2);
		PipelineWork review = await rig.ClaimAtAsync(RoundKind.Review);
		PipelineWork? second = await rig.RecordAsync(review, RoundResult.Errored, "The reviewer crashed");
		Assert.NotNull(second);
		Assert.Equal(2, second.Attempt);
		Assert.Null(await rig.RecordAsync(second, RoundResult.Errored, "The reviewer crashed again"));
		WorkCard? stopped = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stopped);
		Assert.Equal(CardState.Error, stopped.State);
		Assert.Equal(2, stopped.Attempt);
		Assert.Equal(RoundKind.Review, stopped.ResumeRound);
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Review, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
		Assert.NotNull(resumed.Agent);
		Assert.NotNull(resumed.Briefing);
		Assert.Equal(["Review@1:Errored", "Review@2:Errored", "Review@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(3));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Review, stored.Lane);
		Assert.Equal(1, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAStoppedCardWasEditedAndRetried_StartsTheEditedTaskAtWorkNotWhereItStopped()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await rig.RecordAsync(merge, RoundResult.Blocked, "The target branch has local changes in the way.");
		WorkCard? stopped = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stopped);
		Assert.Equal(RoundKind.Merge, stopped.ResumeRound);
		CardInput edited = await TestCardInput.CreateAsync(rig.Database, "Card, reworded", description: "A different task.", reviewerAgentId: TestData.DefaultAgent);

		ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, edited, Token));

		WorkCard? afterEdit = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(afterEdit);
		Assert.Null(afterEdit.ResumeRound);
		Assert.Equal(CardState.Error, afterEdit.State);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(Token));
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(1, work.Attempt);
		Assert.NotNull(work.Briefing);
		Assert.Contains("A different task.", work.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Blocked", "Work@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterABlockedWorkRoundIsRetried_BriefsTheWorkerOnWhyThePreviousAttemptWasBlocked()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Blocked, "The task does not say which database to use.");
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.NotNull(resumed.Briefing);
		string previous = BriefingSections.Get(resumed.Briefing, "How the previous attempt ended");
		Assert.Contains("Round 1, attempt 1, Work, Blocked", previous, StringComparison.Ordinal);
		Assert.Contains("The task does not say which database to use.", previous, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RetryAsync_GivesTheCardAFreshSetOfAttemptsCountedFromOneAgain()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", attemptLimit: 2);
		PipelineWork work = await rig.ClaimAsync();
		PipelineWork? second = await rig.RecordAsync(await rig.SucceedAsync(work), RoundResult.Failed, "Build failed");
		Assert.NotNull(second);
		Assert.Null(await rig.RecordAsync(await rig.SucceedAsync(second), RoundResult.Failed, "Build failed again"));
		WorkCard? stopped = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stopped);
		Assert.Equal(2, stopped.Attempt);
		Assert.Null(stopped.ResumeRound);
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork fresh = await rig.ClaimAsync();
		PipelineWork? afterOneSetback = await rig.RecordAsync(await rig.SucceedAsync(fresh), RoundResult.Failed, "Build failed once more");

		Assert.Equal(RoundKind.Work, fresh.Kind);
		Assert.Equal(1, fresh.Attempt);
		Assert.NotNull(afterOneSetback);
		Assert.Equal(2, afterOneSetback.Attempt);
		Assert.Equal(["Work@1:Completed", "Gate@1:Failed", "Work@2:Completed", "Gate@2:Failed", "Work@1:Completed", "Gate@1:Failed", "Work@2:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		Assert.Equal([1, 2, 3, 4, 5, 6, 7], (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Select(round => round.Sequence));
	}

	[Fact]
	public async Task ClaimNextAsync_AfterARetry_TakesTheRetriedCardBeforeTheCardsThatWaitedBehindIt()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard stopped = await rig.CreateCardAsync("Stopped", attemptLimit: 1);
		WorkCard waiting = await rig.CreateCardAsync("Waiting");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		Assert.Equal(stopped.Id, gate.CardId);
		await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");
		Assert.Equal("Stopped,Waiting", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(stopped.Id, Token));

		PipelineWork first = await rig.ClaimAsync();
		PipelineWork second = await rig.ClaimAsync();

		Assert.Equal(stopped.Id, first.CardId);
		Assert.Equal(waiting.Id, second.CardId);
		Assert.Equal(RoundKind.Work, first.Kind);
	}
}
