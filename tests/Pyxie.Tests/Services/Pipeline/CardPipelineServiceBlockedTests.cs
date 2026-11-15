using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A blocked round needs a person, so it stops the card at once whatever attempt it is on. A round the pipeline cannot open at all, because no worker is assigned or no repository is connected, is recorded as blocked and stops the card the same way.</summary>
public sealed class CardPipelineServiceBlockedTests
{
	private const string NoteRequired = "A note is required when a round fails, is rejected, conflicts, errors, or is blocked.";
	private const string NoWorkerNote = "No worker is assigned.";
	private const string NoRepositoryNote = "No repository is connected.";

	[Theory]
	[InlineData(RoundKind.Work, null, 1)]
	[InlineData(RoundKind.Gate, RoundKind.Gate, 1)]
	[InlineData(RoundKind.Review, RoundKind.Review, 1)]
	[InlineData(RoundKind.Merge, RoundKind.Merge, 1)]
	[InlineData(RoundKind.Work, null, 3)]
	[InlineData(RoundKind.Gate, RoundKind.Gate, 3)]
	[InlineData(RoundKind.Review, RoundKind.Review, 3)]
	[InlineData(RoundKind.Merge, RoundKind.Merge, 3)]
	public async Task RecordResultAsync_OfABlock_StopsTheCardAtOnceOnAnyAttemptResumingAtTheBlockedStage(RoundKind kind, RoundKind? resumeRound, int attempt)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 5);
		PipelineWork running = await PipelineArrangement.ClaimAtAttemptAsync(rig, kind, attempt);
		int roundsBefore = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Count;

		RoundReport report = new("  Needs the staging credentials.  ", "Details");
		ServiceResult<PipelineWork?> result = await rig.Pipeline.RecordResultAsync(running.RoundId, RoundResult.Blocked, report, TestContext.Current.CancellationToken);

		Assert.True(result.Succeeded, $"A stop is a success, but got: {result.Error}");
		Assert.Null(result.Value);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(0, stored.SortOrder);
		Assert.Equal(attempt, stored.Attempt);
		Assert.Equal(resumeRound, stored.ResumeRound);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(roundsBefore, rounds.Count);
		CardRound blocked = rounds[^1];
		Assert.Equal(running.RoundId, blocked.Id);
		Assert.Equal(attempt, blocked.Attempt);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal("Needs the staging credentials.", blocked.Note);
		Assert.Equal("Details", blocked.Report);
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
	}

	[Theory]
	[InlineData(RoundKind.Work, null)]
	[InlineData(RoundKind.Gate, "")]
	[InlineData(RoundKind.Review, "   ")]
	[InlineData(RoundKind.Merge, null)]
	public async Task RecordResultAsync_OfABlockWithoutANote_IsRefusedAndTheCardKeepsRunning(RoundKind kind, string? note)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(running.RoundId, RoundResult.Blocked, new RoundReport(note, "Details"), TestContext.Current.CancellationToken));

		Assert.Equal(NoteRequired, message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		CardRound open = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(running.RoundId, open.Id);
		Assert.Null(open.Outcome);
		PipelineWork? afterwards = await rig.RecordAsync(running, RoundResult.Blocked, "Needs the staging credentials.");
		Assert.Null(afterwards);
	}

	[Theory]
	[InlineData(RoundKind.Work, RoundKind.Gate)]
	[InlineData(RoundKind.Gate, RoundKind.Review)]
	[InlineData(RoundKind.Review, RoundKind.Merge)]
	public async Task RecordResultAsync_WhenTheRepositoryIsGone_RecordsTheNextRoundBlockedStopsTheCardAndResumesThereOnceReconnected(RoundKind finishing, RoundKind blockedKind)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(finishing);
		await rig.RemoveRepositoryAsync();
		DateTime recordedAt = rig.Database.Clock.Tick();

		ServiceResult<PipelineWork?> result = await rig.Pipeline.RecordResultAsync(running.RoundId, RoundResult.Succeeded, new RoundReport(null, null), TestContext.Current.CancellationToken);

		Assert.True(result.Succeeded, $"A stop is a success, but got: {result.Error}");
		Assert.Null(result.Value);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(blockedKind, stored.ResumeRound);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		CardRound blocked = rounds[^1];
		Assert.Equal(blockedKind, blocked.Kind);
		Assert.Equal(1, blocked.Attempt);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal(NoRepositoryNote, blocked.Note);
		Assert.Null(blocked.Agent);
		Assert.Null(blocked.Briefing);
		Assert.Equal(recordedAt, blocked.StartedUtc);
		Assert.Equal(recordedAt, blocked.EndedUtc);
		Assert.Equal(running.RoundId, rounds[^2].Id);
		Assert.NotNull(rounds[^2].Outcome);

		TestData.ConnectRepository(rig.Database);
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));
		PipelineWork resumed = await rig.ClaimAsync();
		Assert.Equal(blockedKind, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
	}

	[Fact]
	public async Task RecordResultAsync_WhenTheRepositoryIsGone_BlocksTheGateStageEvenWhenNoGateIsEnabled()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RemoveRepositoryAsync();

		PipelineWork? next = await rig.RecordAsync(work, RoundResult.Succeeded);

		Assert.Null(next);
		Assert.Equal(["Work@1:Completed", "Gate@1:Blocked"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(RoundKind.Gate, stored.ResumeRound);
	}

	[Fact]
	public async Task ClaimNextAsync_OfACardWithoutAWorker_RecordsABlockedWorkRoundStopsItAtTheTopAndClaimsTheNextReadyCardInTheSameCall()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		DateTime claimedAt = rig.Database.Clock.Tick();
		WorkCard noWorker = await TestData.AddCardAsync(rig.Database, "No worker", workerAgentId: null, reviewerAgentId: TestData.DefaultAgent);
		WorkCard next = await TestData.AddCardAsync(rig.Database, "Next");

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(next.Id, work.CardId);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(1, work.Attempt);
		WorkCard? stopped = await TestData.FindCardAsync(rig.Database, noWorker.Id);
		Assert.NotNull(stopped);
		Assert.Equal(CardLane.Queue, stopped.Lane);
		Assert.Equal(CardState.Error, stopped.State);
		Assert.Equal(0, stopped.SortOrder);
		Assert.Equal(1, stopped.Attempt);
		Assert.Null(stopped.ResumeRound);
		Assert.Equal(noWorker.Version + 1, stopped.Version);
		CardRound blocked = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, noWorker.Id));
		Assert.Equal(RoundKind.Work, blocked.Kind);
		Assert.Equal(1, blocked.Attempt);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal(NoWorkerNote, blocked.Note);
		Assert.Null(blocked.Agent);
		Assert.Null(blocked.Briefing);
		Assert.Equal(claimedAt, blocked.StartedUtc);
		Assert.Equal(claimedAt, blocked.EndedUtc);
		Assert.Equal("No worker", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("Next", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
		Assert.Equal(["Work@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, next.Id));
	}

	[Fact]
	public async Task ClaimNextAsync_WhenOnlyCardsWithoutAWorkerWait_StopsThemAndReturnsNull()
	{
		await using PipelineRig rig = new();
		WorkCard first = await TestData.AddCardAsync(rig.Database, "First", workerAgentId: null);
		WorkCard second = await TestData.AddCardAsync(rig.Database, "Second", workerAgentId: null);

		PipelineWork? work = await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);

		Assert.Null(work);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		foreach (WorkCard card in new[] { first, second })
		{
			WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
			Assert.NotNull(stored);
			Assert.Equal(CardState.Error, stored.State);
			Assert.Equal(CardLane.Queue, stored.Lane);
			Assert.Equal(["Work@1:Blocked"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		}

		Assert.Equal("Second,First", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
	}

	[Fact]
	public async Task ClaimNextAsync_OfCardsWithoutAWorkerAheadOfAReadyCard_StopsEachOneAndStillClaimsTheReadyCard()
	{
		await using PipelineRig rig = new();
		WorkCard first = await TestData.AddCardAsync(rig.Database, "First", workerAgentId: null);
		WorkCard second = await TestData.AddCardAsync(rig.Database, "Second", workerAgentId: null);
		WorkCard ready = await TestData.AddCardAsync(rig.Database, "Ready");

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(ready.Id, work.CardId);
		Assert.Equal("Second,First", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
		Assert.Equal(["Work@1:Blocked"], await PipelineRoundLog.ReadAsync(rig.Database, first.Id));
		Assert.Equal(["Work@1:Blocked"], await PipelineRoundLog.ReadAsync(rig.Database, second.Id));
	}

	[Fact]
	public async Task ClaimNextAsync_AfterARetryThatDidNotAssignAWorker_BlocksTheCardAgainWithAFreshAttempt()
	{
		await using PipelineRig rig = new();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "No worker", workerAgentId: null);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		Assert.Equal(["Work@1:Blocked", "Work@1:Blocked"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(1, stored.Attempt);
	}
}
