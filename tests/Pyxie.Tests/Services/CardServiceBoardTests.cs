using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>The board shows every card with what its rounds add up to, but never the briefings and reports behind them, and never closed cards.</summary>
public sealed class CardServiceBoardTests
{
	[Fact]
	public async Task GetBoardAsync_SummarizesEachCardsRoundsAndLeavesCardsWithoutRoundsEmpty()
	{
		await using TestDatabase database = new();
		WorkCard fresh = await TestData.AddCardAsync(database, "Fresh", CardLane.Queue);
		WorkCard exhausted = await TestData.AddCardAsync(database, "Exhausted", CardLane.Queue, state: CardState.Error, attempt: 3);
		await TestData.AddRoundAsync(database, exhausted.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda", attempt: 1);
		await TestData.AddRoundAsync(database, exhausted.Id, 2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", attempt: 1);
		await TestData.AddRoundAsync(database, exhausted.Id, 3, RoundKind.Work, RoundOutcome.Completed, "Walt", attempt: 2);
		await TestData.AddRoundAsync(database, exhausted.Id, 4, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", attempt: 2);
		await TestData.AddRoundAsync(database, exhausted.Id, 5, RoundKind.Work, RoundOutcome.Completed, "Wilma", attempt: 3);
		await TestData.AddRoundAsync(database, exhausted.Id, 6, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", attempt: 3);
		WorkCard blocked = await TestData.AddCardAsync(database, "Blocked", CardLane.Queue, state: CardState.Error, attempt: 1);
		await TestData.AddRoundAsync(database, blocked.Id, 1, RoundKind.Work, RoundOutcome.Blocked, "Wanda", note: "Needs the API key.");
		WorkCard looping = await TestData.AddCardAsync(database, "Looping", CardLane.InProgress, attempt: 2);
		await TestData.AddRoundAsync(database, looping.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, looping.Id, 2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed");
		await TestData.AddRoundAsync(database, looping.Id, 3, RoundKind.Work, outcome: null, "Walt", attempt: 2);
		WorkCard done = await TestData.AddCardAsync(database, "Done", CardLane.Complete, attempt: 2);
		await TestData.AddRoundAsync(database, done.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, done.Id, 2, RoundKind.Gate, RoundOutcome.Passed);
		await TestData.AddRoundAsync(database, done.Id, 3, RoundKind.Review, RoundOutcome.Rejected, "Rita", note: "Missing a test");
		await TestData.AddRoundAsync(database, done.Id, 4, RoundKind.Work, RoundOutcome.Completed, "Walt", attempt: 2);
		await TestData.AddRoundAsync(database, done.Id, 5, RoundKind.Gate, RoundOutcome.Passed, attempt: 2);
		await TestData.AddRoundAsync(database, done.Id, 6, RoundKind.Review, RoundOutcome.Approved, "Ray", attempt: 2);
		await TestData.AddRoundAsync(database, done.Id, 7, RoundKind.Merge, RoundOutcome.Merged, attempt: 2);
		WorkCard skippedStages = await TestData.AddCardAsync(database, "Unchecked", CardLane.Complete);
		await TestData.AddRoundAsync(database, skippedStages.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, skippedStages.Id, 2, RoundKind.Gate, RoundOutcome.Skipped, note: "No gates are enabled.");
		await TestData.AddRoundAsync(database, skippedStages.Id, 3, RoundKind.Review, RoundOutcome.Skipped, note: "No reviewer is assigned.");
		await TestData.AddRoundAsync(database, skippedStages.Id, 4, RoundKind.Merge, RoundOutcome.Merged);
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Fresh", "Exhausted", "Blocked", "Looping", "Done", "Unchecked"], board.Select(entry => entry.Card.Name));
		Assert.Same(CardHistorySummary.Empty, board.Single(entry => entry.Card.Id == fresh.Id).History);
		Assert.Equal(new CardHistorySummary(3, 3, 0, 0, 3, false, "Stopped after 3 attempts. Gates Failed: Build failed"), board.Single(entry => entry.Card.Id == exhausted.Id).History);
		Assert.Equal(new CardHistorySummary(1, 0, 0, 0, 0, false, "Needs the API key."), board.Single(entry => entry.Card.Id == blocked.Id).History);
		Assert.Equal(new CardHistorySummary(2, 1, 0, 0, 1, true, null), board.Single(entry => entry.Card.Id == looping.Id).History);
		Assert.Equal(new CardHistorySummary(2, 2, 2, 0, 1, false, null), board.Single(entry => entry.Card.Id == done.Id).History);
		Assert.Equal(new CardHistorySummary(1, 0, 0, 0, 0, false, null), board.Single(entry => entry.Card.Id == skippedStages.Id).History);

		IReadOnlyList<BoardCardSummary> page = await service.GetBoardPageAsync(0, board.Count, TestContext.Current.CancellationToken);
		Assert.Equal(board.OrderBy(row => row.Card.Id).Select(row => row.History), page.Select(row => row.History));
	}

	[Fact]
	public async Task BoardPageContinuesByCardIdAfterAnEarlierCardIsClosed()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using TestDatabase database = new();
		WorkCard first = await TestData.AddCardAsync(database, "First", CardLane.Complete);
		await TestData.AddCardAsync(database, "Closed", CardLane.Closed);
		WorkCard next = await TestData.AddCardAsync(database, "Next", CardLane.Queue);
		CardService service = new(database.ContextFactory, database.Clock);

		Assert.Equal(first.Id, Assert.Single(await service.GetBoardPageAsync(0, 1, token)).Card.Id);
		ResultAssert.Succeeded(await service.CloseAsync(first.Id, token));
		Assert.Equal(next.Id, Assert.Single(await service.GetBoardPageAsync(first.Id, 1, token)).Card.Id);
		Assert.Empty(await service.GetBoardPageAsync(next.Id, 1, token));
	}

	[Fact]
	public async Task GetBoardAsync_GivesAStoppedCardWithoutRoundsTheGenericStopReason_AndAReadyCardNone()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error);
		WorkCard ready = await TestData.AddCardAsync(database, "Ready");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		Assert.Equal("Stopped by the pipeline.", board.Single(entry => entry.Card.Id == stopped.Id).History.StopReason);
		Assert.Null(board.Single(entry => entry.Card.Id == ready.Id).History.StopReason);
		Assert.Equal(CardState.Error, board.Single(entry => entry.Card.Id == stopped.Id).Card.State);
	}

	[Fact]
	public async Task GetBoardAsync_LeavesClosedCardsAndTheirRoundsOff()
	{
		await using TestDatabase database = new();
		WorkCard open = await TestData.AddCardAsync(database, "Open", CardLane.Complete);
		WorkCard closed = await TestData.AddCardAsync(database, "Closed", CardLane.Closed);
		await TestData.AddRoundAsync(database, open.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, closed.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		BoardCard only = Assert.Single(board);
		Assert.Equal(open.Id, only.Card.Id);
		Assert.Equal(1, only.History.WorkRounds);
	}

	[Fact]
	public async Task GetBoardAsync_KeepsTheCategoryAndAgentsOfEachCardAndTheRoundsOfEachCardApart()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		WorkCard first = await TestData.AddCardAsync(database, "First", CardLane.InProgress, workerAgentId: worker.Id);
		WorkCard second = await TestData.AddCardAsync(database, "Second", CardLane.InProgress, workerAgentId: null);
		await TestData.AddRoundAsync(database, first.Id, 1, RoundKind.Work, outcome: null, "Worker");
		await TestData.AddRoundAsync(database, second.Id, 1, RoundKind.Work, RoundOutcome.Errored, "Other", note: "Crashed");
		await TestData.AddRoundAsync(database, second.Id, 2, RoundKind.Work, outcome: null, "Other", attempt: 2);
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		BoardCard one = board.Single(entry => entry.Card.Id == first.Id);
		BoardCard two = board.Single(entry => entry.Card.Id == second.Id);
		Assert.Equal("Feature", one.Card.Category.Name);
		Assert.Equal("Worker", one.Card.WorkerAgent?.Name);
		Assert.Equal(1, one.History.WorkRounds);
		Assert.Equal("Worker", one.Rounds[^1].AgentName);
		Assert.Equal(2, two.History.WorkRounds);
		Assert.Equal("Other", two.Rounds[^1].AgentName);
		Assert.Null(two.Card.WorkerAgent);
	}

	[Fact]
	public async Task GetBoardAsync_GivesEachCardItsRoundsOldestFirstWithTheirKindOutcomeAgentAndTimes()
	{
		await using TestDatabase database = new();
		DateTime start = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
		WorkCard fresh = await TestData.AddCardAsync(database, "Fresh", CardLane.Queue);
		WorkCard busy = await TestData.AddCardAsync(database, "Busy", CardLane.Gates, attempt: 2);
		WorkCard other = await TestData.AddCardAsync(database, "Other", CardLane.InProgress);

		// Stored out of order, so the order the board returns can only come from the sequence.
		await TestData.AddRoundAsync(database, busy.Id, 3, RoundKind.Gate, outcome: null, startedUtc: start.AddMinutes(30));
		await TestData.AddRoundAsync(database, busy.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda", note: "Done", report: "A long handoff", briefing: "A long briefing", startedUtc: start, endedUtc: start.AddMinutes(10));
		await TestData.AddRoundAsync(database, other.Id, 1, RoundKind.Work, outcome: null, "Walt", startedUtc: start.AddHours(1));
		await TestData.AddRoundAsync(database, busy.Id, 2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", attempt: 1, startedUtc: start.AddMinutes(12), endedUtc: start.AddMinutes(20));
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		Assert.Empty(board.Single(entry => entry.Card.Id == fresh.Id).Rounds);
		IReadOnlyList<RoundSummary> trail = board.Single(entry => entry.Card.Id == busy.Id).Rounds;
		Assert.Equal([1, 2, 3], trail.Select(round => round.Sequence));
		Assert.Equal([RoundKind.Work, RoundKind.Gate, RoundKind.Gate], trail.Select(round => round.Kind));
		Assert.Equal([RoundOutcome.Completed, RoundOutcome.Failed, null], trail.Select(round => round.Outcome));
		Assert.Equal(["Wanda", null, null], trail.Select(round => round.AgentName));
		Assert.Equal([start, start.AddMinutes(12), start.AddMinutes(30)], trail.Select(round => round.StartedUtc));
		Assert.Equal([start.AddMinutes(10), start.AddMinutes(20), (DateTime?)null], trail.Select(round => round.EndedUtc));
		Assert.Equal(["Done", "Build failed", null], trail.Select(round => round.Note));
		Assert.All(trail, round => Assert.Equal(busy.Id, round.CardId));
		RoundSummary alone = Assert.Single(board.Single(entry => entry.Card.Id == other.Id).Rounds);
		Assert.Equal(("Walt", start.AddHours(1), (DateTime?)null), (alone.AgentName, alone.StartedUtc, alone.EndedUtc));
	}

	[Fact]
	public async Task GetBoardAsync_AfterARealPipelineRun_ShowsWhoWorkedAndWhatIsRunning()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Rita", canWork: false, canReview: true);
		await rig.CreateCardAsync("Card", worker.Id, reviewer.Id);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		PipelineWork? work = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");
		Assert.NotNull(work);

		BoardCard onBoard = Assert.Single(await rig.Cards.GetBoardAsync(TestContext.Current.CancellationToken));

		Assert.Equal(CardLane.InProgress, onBoard.Card.Lane);
		Assert.Equal(CardState.Ready, onBoard.Card.State);
		Assert.Equal(2, onBoard.Card.Attempt);
		Assert.Equal(new CardHistorySummary(2, 1, 0, 0, 1, true, null), onBoard.History);
	}

	[Fact]
	public async Task GetBoardAsync_AfterThePipelineStopsACard_ShowsItInErrorWithTheReasonItGave()
	{
		await using PipelineRig rig = new();
		await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Blocked, "Needs the staging credentials.");

		BoardCard onBoard = Assert.Single(await rig.Cards.GetBoardAsync(TestContext.Current.CancellationToken));

		Assert.Equal(CardLane.Queue, onBoard.Card.Lane);
		Assert.Equal(CardState.Error, onBoard.Card.State);
		Assert.False(onBoard.History.IsRunning, "The stopped card has no running round.");
		Assert.Equal("Needs the staging credentials.", onBoard.History.StopReason);
	}
}
