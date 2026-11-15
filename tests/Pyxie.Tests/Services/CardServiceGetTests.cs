using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Reading one card returns it wherever it is, closed cards included, which the card dialog relies on to show where a card really is.</summary>
public sealed class CardServiceGetTests
{
	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	[InlineData(CardLane.Complete)]
	[InlineData(CardLane.Closed)]
	public async Task GetAsync_ReturnsTheCardInAnyLaneWithItsCategoryAndAgents(CardLane lane)
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, PyxieSeedData.BugCategoryId, worker.Id, reviewer.Id, "Some text");
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard found = ResultAssert.Succeeded(await service.GetAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal(lane, found.Lane);
		Assert.Equal("Card", found.Name);
		Assert.Equal("Some text", found.Description);
		Assert.Equal("Bug", found.Category.Name);
		Assert.Equal("Worker", found.WorkerAgent?.Name);
		Assert.Equal("Reviewer", found.ReviewerAgent?.Name);
		Assert.Equal(card.ClosedUtc, found.ClosedUtc);
	}

	[Fact]
	public async Task GetAsync_ReturnsTheStateAttemptsLimitAndResumeStageOfAStoppedCard()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, attemptLimit: 7, resumeRound: RoundKind.Review);
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard found = ResultAssert.Succeeded(await service.GetAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal(CardState.Error, found.State);
		Assert.Equal(3, found.Attempt);
		Assert.Equal(7, found.AttemptLimit);
		Assert.Equal(RoundKind.Review, found.ResumeRound);
		Assert.Equal(CardLane.Queue, found.Lane);
	}

	[Fact]
	public async Task GetAsync_OfAnUnknownCard_ReportsNotFound()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Other");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.GetAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
	}
}
