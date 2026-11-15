using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A card's history is its rounds, oldest first, with the briefings, notes, and reports the board leaves out.</summary>
public sealed class CardServiceHistoryTests
{
	[Fact]
	public async Task GetHistoryAsync_ReturnsTheRoundsOldestFirstWhateverOrderTheyWereStored()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.Review);
		await TestData.AddRoundAsync(database, card.Id, 3, RoundKind.Review, outcome: null, "Rita");
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, card.Id, 2, RoundKind.Gate, RoundOutcome.Passed);
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal([1, 2, 3], history.Select(round => round.Sequence));
		Assert.Equal([RoundKind.Work, RoundKind.Gate, RoundKind.Review], history.Select(round => round.Kind));
		Assert.Equal([RoundOutcome.Completed, RoundOutcome.Passed, null], history.Select(round => round.Outcome));
	}

	[Fact]
	public async Task GetHistoryAsync_IncludesTheBriefingNoteReportAgentAndTimesOfEachRound()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.InProgress);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Gate, RoundOutcome.Failed, note: "2 tests failed", report: "FAIL A\nFAIL B");
		await TestData.AddRoundAsync(database, card.Id, 2, RoundKind.Work, outcome: null, "Wanda", briefing: "# Card 1: Card\n\nThe task.");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		CardRound gate = history[0];
		Assert.Equal("2 tests failed", gate.Note);
		Assert.Equal("FAIL A\nFAIL B", gate.Report);
		Assert.Null(gate.Agent);
		Assert.Null(gate.Briefing);
		Assert.NotNull(gate.EndedUtc);
		Assert.Equal(DateTimeKind.Utc, gate.StartedUtc.Kind);
		CardRound work = history[1];
		Assert.Equal("# Card 1: Card\n\nThe task.", work.Briefing);
		Assert.NotNull(work.Agent);
		Assert.Equal("Wanda", work.Agent.Name);
		Assert.Equal("claude", work.Agent.Command);
		Assert.Null(work.EndedUtc);
		Assert.Null(work.Outcome);
	}

	[Fact]
	public async Task GetHistoryAsync_IncludesTheAttemptOfEachRound()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.InProgress, attempt: 2);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda", attempt: 1);
		await TestData.AddRoundAsync(database, card.Id, 2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed", attempt: 1);
		await TestData.AddRoundAsync(database, card.Id, 3, RoundKind.Work, outcome: null, "Wanda", attempt: 2);
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal([1, 1, 2], history.Select(round => round.Attempt));
	}

	[Fact]
	public async Task GetHistoryAsync_ReturnsOnlyTheRoundsOfTheCardAsked()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", CardLane.Gates);
		WorkCard other = await TestData.AddCardAsync(database, "Other", CardLane.Gates);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, other.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Walt");
		await TestData.AddRoundAsync(database, other.Id, 2, RoundKind.Gate, outcome: null);
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		CardRound only = Assert.Single(history);
		Assert.Equal("Wanda", only.Agent?.Name);
		Assert.Equal(card.Id, only.CardId);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.Complete)]
	[InlineData(CardLane.Closed)]
	public async Task GetHistoryAsync_WorksForCardsInAnyLane(CardLane lane)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Single(history);
	}

	[Fact]
	public async Task GetHistoryAsync_OfACardWithoutRounds_IsAnEmptySuccess()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Empty(history);
	}

	[Fact]
	public async Task GetHistoryAsync_OfAnUnknownCard_ReportsNotFound()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.GetHistoryAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
	}

	[Fact]
	public async Task GetHistoryAsync_AfterARealPipelineRun_HoldsTheBriefingsThatEachAgentWasGiven()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", description: "Make the login accept plus signs.");
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		PipelineWork? work = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed", "CS1002");
		Assert.NotNull(work);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await rig.Cards.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal(3, history.Count);
		Assert.Contains("Make the login accept plus signs.", history[0].Briefing, StringComparison.Ordinal);
		Assert.Null(history[1].Briefing);
		Assert.Equal("CS1002", history[1].Report);
		Assert.Equal(work.Briefing, history[2].Briefing);
		Assert.Contains("Build failed", history[2].Briefing, StringComparison.Ordinal);
	}
}
