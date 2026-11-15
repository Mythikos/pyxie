using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CardServiceTests
{
	[Fact]
	public async Task CreateAsync_AppendsToBottomOfQueue()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		WorkCard first = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "A"), token));
		WorkCard second = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "B"), token));
		WorkCard third = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "C"), token));

		Assert.Equal(CardLane.Queue, first.Lane);
		Assert.Equal("0,1,2", string.Join(",", first.SortOrder, second.SortOrder, third.SortOrder));
		Assert.Equal("A,B,C", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Queue));
	}

	[Fact]
	public async Task CreateAsync_IgnoresOtherLanesWhenOrdering()
	{
		await using TestDatabase database = new();
		await TestData.AddLaneAsync(database, CardLane.Review, "T1", "T2");
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard card = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "New"), TestContext.Current.CancellationToken));

		Assert.Equal(CardLane.Queue, card.Lane);
		Assert.Equal(0, card.SortOrder);
		Assert.Equal("T1,T2", await TestData.ReadLaneNamesAsync(database, CardLane.Review));
	}

	[Fact]
	public async Task CreateAsync_PersistsFieldsAndTimestamps()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true);
		CardService service = new(database.ContextFactory, database.Clock);
		DateTime createdAt = database.Clock.Tick();

		WorkCard created = ResultAssert.Succeeded(await service.CreateAsync(TestCardInput.Create("Login page", categoryId: PyxieSeedData.FeatureCategoryId, workerAgentId: worker.Id, reviewerAgentId: reviewer.Id, description: "Build it"), TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, created.Id);
		Assert.NotNull(stored);
		Assert.Equal("Login page", stored.Name);
		Assert.Equal("Build it", stored.Description);
		Assert.Equal(PyxieSeedData.FeatureCategoryId, stored.CategoryId);
		Assert.Equal(worker.Id, stored.WorkerAgentId);
		Assert.Equal(reviewer.Id, stored.ReviewerAgentId);
		Assert.Equal(createdAt, stored.CreatedUtc);
		Assert.Equal(createdAt, stored.UpdatedUtc);
	}

	[Fact]
	public async Task CreateAsync_StartsTheCardReadyWithNoAttemptsAndNoResumeStage()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard created = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card"), TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, created.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(0, stored.Attempt);
		Assert.Null(stored.ResumeRound);
		Assert.Equal(PyxieLimits.DefaultAttemptLimit, stored.AttemptLimit);
	}

	[Fact]
	public async Task CreateAsync_AllowsCardWithoutAReviewer()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard card = ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Loose idea"), TestContext.Current.CancellationToken));

		Assert.Equal(PyxieSeedData.FeatureCategoryId, card.CategoryId);
		Assert.NotNull(card.WorkerAgentId);
		Assert.Null(card.ReviewerAgentId);
		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.NotNull(stored.WorkerAgentId);
		Assert.Null(stored.ReviewerAgentId);
	}

	[Fact]
	public async Task CreateAsync_RefusesACardWithoutAWorker()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Loose idea"), TestContext.Current.CancellationToken));

		Assert.Equal("Worker is required.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_AllowsAgentWithBothRolesAsWorkerAndReviewer()
	{
		await using TestDatabase database = new();
		Agent both = await TestData.AddAgentAsync(database, "Both", canWork: true, canReview: true);
		CardService service = new(database.ContextFactory, database.Clock);

		WorkCard card = ResultAssert.Succeeded(await service.CreateAsync(TestCardInput.Create("Card", workerAgentId: both.Id, reviewerAgentId: both.Id), TestContext.Current.CancellationToken));

		Assert.Equal(both.Id, card.WorkerAgentId);
		Assert.Equal(both.Id, card.ReviewerAgentId);
	}

	[Fact]
	public async Task CreateAsync_RejectsNameLongerThanLimit()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.CreateAsync(await TestCardInput.CreateAsync(database, new string('n', PyxieLimits.MaxNameLength + 1)), token));
		ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, new string('n', PyxieLimits.MaxNameLength)), token));

		Assert.Equal(1, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_RejectsDescriptionLongerThanLimit()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Failed(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", description: new string('d', PyxieLimits.MaxDescriptionLength + 1)), token));
		ResultAssert.Succeeded(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", description: new string('d', PyxieLimits.MaxDescriptionLength)), token));

		Assert.Equal(1, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_RejectsUnknownWorker()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Card", workerAgentId: 999), TestContext.Current.CancellationToken));

		Assert.Equal("Worker agent not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_RejectsUnknownReviewer()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", reviewerAgentId: 999), TestContext.Current.CancellationToken));

		Assert.Equal("Reviewer agent not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	public async Task CreateAsync_RejectsWorkerWithoutCanWork(bool canWork, bool canReview)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork, canReview);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(TestCardInput.Create("Card", workerAgentId: agent.Id), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' cannot be assigned as a worker.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	public async Task CreateAsync_RejectsReviewerWithoutCanReview(bool canWork, bool canReview)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Agent", canWork, canReview);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.CreateAsync(await TestCardInput.CreateAsync(database, "Card", reviewerAgentId: agent.Id), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Agent' cannot be assigned as a reviewer.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task UpdateAsync_ChangesFieldsStampsUpdatedUtcAndKeepsLaneAndOrder()
	{
		await using TestDatabase database = new();
		DateTime arranged = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		WorkCard first = await TestData.AddCardAsync(database, "X", stampedUtc: arranged);
		WorkCard original = await TestData.AddCardAsync(database, "Y", stampedUtc: arranged);
		WorkCard last = await TestData.AddCardAsync(database, "Z", stampedUtc: arranged);
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true);
		CardService service = new(database.ContextFactory, database.Clock);
		DateTime updatedAt = database.Clock.Tick();

		WorkCard updated = ResultAssert.Succeeded(await service.UpdateAsync(original.Id, TestCardInput.Create("Renamed", categoryId: PyxieSeedData.BugCategoryId, workerAgentId: worker.Id, reviewerAgentId: reviewer.Id, description: "New text"), TestContext.Current.CancellationToken));

		Assert.Equal(original.Id, updated.Id);
		Assert.Equal(updatedAt, updated.UpdatedUtc);
		WorkCard? stored = await TestData.FindCardAsync(database, original.Id);
		Assert.NotNull(stored);
		Assert.Equal("Renamed", stored.Name);
		Assert.Equal("New text", stored.Description);
		Assert.Equal(PyxieSeedData.BugCategoryId, stored.CategoryId);
		Assert.Equal(worker.Id, stored.WorkerAgentId);
		Assert.Equal(reviewer.Id, stored.ReviewerAgentId);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(1, stored.SortOrder);
		Assert.Equal(arranged, stored.CreatedUtc);
		Assert.Equal(updatedAt, stored.UpdatedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.UpdatedUtc.Kind);
		Assert.Equal("X,Renamed,Z", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		await CardAssert.UnchangedAsync(database, first, expectedCardCount: 3);
		await CardAssert.UnchangedAsync(database, last, expectedCardCount: 3);
	}

	[Fact]
	public async Task UpdateAsync_CanClearTheReviewerButKeepsCategoryAndTheWorker()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Card", categoryId: PyxieSeedData.BugCategoryId, workerAgentId: worker.Id, reviewerAgentId: worker.Id);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(card.Id, TestCardInput.Create("Card", categoryId: PyxieSeedData.BugCategoryId, workerAgentId: worker.Id), TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(PyxieSeedData.BugCategoryId, stored.CategoryId);
		Assert.Equal(worker.Id, stored.WorkerAgentId);
		Assert.Null(stored.ReviewerAgentId);
	}

	[Fact]
	public async Task UpdateAsync_RefusesClearingTheWorkerAndLeavesTheCardUntouched()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Card"), TestContext.Current.CancellationToken));

		Assert.Equal("Worker is required.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_RejectsUnknownCard()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(999, TestCardInput.Create("Card"), TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
	}

	[Fact]
	public async Task UpdateAsync_RejectsNameLongerThanLimit()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, new string('n', PyxieLimits.MaxNameLength + 1)), TestContext.Current.CancellationToken));

		Assert.StartsWith("Name", message, StringComparison.Ordinal);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_RejectsWorkerWithoutCanWork()
	{
		await using TestDatabase database = new();
		Agent reviewerOnly = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true);
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, TestCardInput.Create("Original", workerAgentId: reviewerOnly.Id), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Reviewer' cannot be assigned as a worker.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task UpdateAsync_RejectsReviewerWithoutCanReview()
	{
		await using TestDatabase database = new();
		Agent workerOnly = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		WorkCard card = await TestData.AddCardAsync(database, "Original");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(card.Id, await TestCardInput.CreateAsync(database, "Original", reviewerAgentId: workerOnly.Id), TestContext.Current.CancellationToken));

		Assert.Equal("Agent 'Worker' cannot be assigned as a reviewer.", message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 1);
	}

	[Fact]
	public async Task DeleteAsync_RemovesCardAndResequencesLane()
	{
		await using TestDatabase database = new();
		Dictionary<string, WorkCard> lane = await TestData.AddLaneAsync(database, CardLane.Queue, "A", "B", "C", "D");
		await TestData.AddLaneAsync(database, CardLane.InProgress, "P", "Q");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(lane["B"].Id, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindCardAsync(database, lane["B"].Id));
		Assert.Equal("A,C,D", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Queue));
		Assert.Equal("P,Q", await TestData.ReadLaneNamesAsync(database, CardLane.InProgress));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(database, CardLane.InProgress));
	}

	[Fact]
	public async Task DeleteAsync_RejectsUnknownCard()
	{
		await using TestDatabase database = new();
		await TestData.AddLaneAsync(database, CardLane.Queue, "A");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Failed(await service.DeleteAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal(1, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task GetBoardAsync_OrdersByLaneThenSortOrder()
	{
		await using TestDatabase database = new();
		await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C2");
		await TestData.AddLaneAsync(database, CardLane.Queue, "B1", "B2", "B3");
		await TestData.AddLaneAsync(database, CardLane.Review, "R1");
		await TestData.AddLaneAsync(database, CardLane.Gates, "G1");
		await TestData.AddLaneAsync(database, CardLane.InProgress, "I1", "I2");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["B1", "B2", "B3", "I1", "I2", "G1", "R1", "C1", "C2"], board.Select(entry => entry.Card.Name));
	}

	[Fact]
	public async Task GetBoardAsync_ExcludesClosedCardsButKeepsThemInTheDatabase()
	{
		await using TestDatabase database = new();
		await TestData.AddLaneAsync(database, CardLane.Queue, "Q1");
		await TestData.AddLaneAsync(database, CardLane.Complete, "C1", "C2");
		await TestData.AddLaneAsync(database, CardLane.Closed, "X1", "X2");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Q1", "C1", "C2"], board.Select(entry => entry.Card.Name));
		Assert.DoesNotContain(board, entry => entry.Card.Lane == CardLane.Closed);
		Assert.Equal(5, await TestData.CountCardsAsync(database));
	}

	[Fact]
	public async Task GetBoardAsync_ReturnsEmptyListForEmptyBoard()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		Assert.Empty(board);
	}

	[Fact]
	public async Task GetBoardAsync_IncludesCategoryAndAgents()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);
		Agent reviewer = await TestData.AddAgentAsync(database, "Reviewer", canWork: false, canReview: true);
		await TestData.AddCardAsync(database, "Assigned", categoryId: PyxieSeedData.BugCategoryId, workerAgentId: worker.Id, reviewerAgentId: reviewer.Id);
		await TestData.AddCardAsync(database, "Unassigned", workerAgentId: null);
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<BoardCard> board = await service.GetBoardAsync(TestContext.Current.CancellationToken);

		WorkCard assigned = Assert.Single(board, entry => entry.Card.Name == "Assigned").Card;
		Assert.Equal("Bug", assigned.Category.Name);
		Assert.Equal("Worker", assigned.WorkerAgent?.Name);
		Assert.Equal("Reviewer", assigned.ReviewerAgent?.Name);
		WorkCard unassigned = Assert.Single(board, entry => entry.Card.Name == "Unassigned").Card;
		Assert.Equal("Feature", unassigned.Category.Name);
		Assert.Null(unassigned.WorkerAgent);
		Assert.Null(unassigned.ReviewerAgent);
	}
}
