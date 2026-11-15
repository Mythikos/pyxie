using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A person retries a card the pipeline stopped: it becomes Ready with a fresh set of attempts, keeps its place at the top of the Queue, and nothing else can be retried.</summary>
public sealed class CardServiceRetryTests
{
	private const string NotStopped = "Only a card the pipeline stopped can be retried.";
	private const string Changed = "The card changed while it was being saved. Try again.";

	[Fact]
	public async Task RetryAsync_MakesAStoppedCardReadyWithAFreshSetOfAttemptsAndKeepsItsPlaceAtTheTopOfTheQueue()
	{
		await using TestDatabase database = new();
		DateTime arranged = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: RoundKind.Gate, stampedUtc: arranged);
		WorkCard second = await TestData.AddCardAsync(database, "Second", stampedUtc: arranged);
		WorkCard third = await TestData.AddCardAsync(database, "Third", stampedUtc: arranged);
		CardService service = new(database.ContextFactory, database.Clock);
		DateTime retriedAt = database.Clock.Tick();

		ResultAssert.Succeeded(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(0, stored.Attempt);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(0, stored.SortOrder);
		Assert.Equal(stopped.Version + 1, stored.Version);
		Assert.Equal(retriedAt, stored.UpdatedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.UpdatedUtc.Kind);
		Assert.Equal("Stopped,Second,Third", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Queue));
		await CardAssert.UnchangedAsync(database, second, expectedCardCount: 3);
		await CardAssert.UnchangedAsync(database, third, expectedCardCount: 3);
	}

	[Fact]
	public async Task RetryAsync_ChangesNothingButTheStateAttemptVersionAndUpdateTime()
	{
		await using TestDatabase database = new();
		Agent reviewer = await TestData.AddAgentAsync(database, "Rita", canWork: false, canReview: true);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", categoryId: PyxieSeedData.BugCategoryId, reviewerAgentId: reviewer.Id, description: "The task", state: CardState.Error, attempt: 7, attemptLimit: 7, resumeRound: RoundKind.Review);
		CardService service = new(database.ContextFactory, database.Clock);
		database.Clock.Tick();

		ResultAssert.Succeeded(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal("Stopped", stored.Name);
		Assert.Equal("The task", stored.Description);
		Assert.Equal(PyxieSeedData.BugCategoryId, stored.CategoryId);
		Assert.Equal(stopped.WorkerAgentId, stored.WorkerAgentId);
		Assert.Equal(reviewer.Id, stored.ReviewerAgentId);
		Assert.Equal(7, stored.AttemptLimit);
		Assert.Equal(stopped.CreatedUtc, stored.CreatedUtc);
		Assert.Null(stored.ClosedUtc);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	[InlineData(RoundKind.Resolve)]
	public async Task RetryAsync_KeepsTheStageTheCardStoppedAtSoThePipelineResumesThere(RoundKind? resumeRound)
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: resumeRound);
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		Assert.Equal(resumeRound, (await TestData.FindCardAsync(database, stopped.Id))?.ResumeRound);
	}

	[Fact]
	public async Task RetryAsync_RefusesAReadyCardInTheQueue()
	{
		await using TestDatabase database = new();
		WorkCard ready = await TestData.AddCardAsync(database, "Ready");
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.RetryAsync(ready.Id, TestContext.Current.CancellationToken));

		Assert.Equal(NotStopped, message);
		await CardAssert.UnchangedAsync(database, ready, expectedCardCount: 1);
	}

	[Theory]
	[InlineData(CardLane.InProgress, CardState.Error)]
	[InlineData(CardLane.Gates, CardState.Error)]
	[InlineData(CardLane.Review, CardState.Error)]
	[InlineData(CardLane.Complete, CardState.Error)]
	[InlineData(CardLane.Closed, CardState.Error)]
	[InlineData(CardLane.InProgress, CardState.Ready)]
	[InlineData(CardLane.Gates, CardState.Ready)]
	[InlineData(CardLane.Review, CardState.Ready)]
	[InlineData(CardLane.Complete, CardState.Ready)]
	[InlineData(CardLane.Closed, CardState.Ready)]
	public async Task RetryAsync_RefusesACardOutsideTheQueueWhateverItsState(CardLane lane, CardState state)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card", lane, state: state, attempt: 2, resumeRound: RoundKind.Gate);
		WorkCard neighbour = await TestData.AddCardAsync(database, "Neighbour", lane);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.RetryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal(NotStopped, message);
		await CardAssert.UnchangedAsync(database, card, expectedCardCount: 2);
		await CardAssert.UnchangedAsync(database, neighbour, expectedCardCount: 2);
	}

	[Fact]
	public async Task RetryAsync_OfAnUnknownCard_ReportsNotFound()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error);
		CardService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.RetryAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("Card not found.", message);
		await CardAssert.UnchangedAsync(database, stopped, expectedCardCount: 1);
	}

	[Fact]
	public async Task RetryAsync_ASecondTime_IsRefusedBecauseTheCardIsReadyNow()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3);
		CardService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.RetryAsync(stopped.Id, token));
		WorkCard? retried = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(retried);

		string message = ResultAssert.Failed(await service.RetryAsync(stopped.Id, token));

		Assert.Equal(NotStopped, message);
		await CardAssert.UnchangedAsync(database, retried, expectedCardCount: 1);
	}

	[Fact]
	public async Task RetryAsync_RefusesWhenTheCardChangesBetweenTheReadAndTheWriteAndLeavesItStopped()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: RoundKind.Gate);
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"UPDATE \"Cards\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = {stopped.Id}");

		string message = ResultAssert.Failed(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal(Changed, message);
		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(3, stored.Attempt);
		Assert.Equal(RoundKind.Gate, stored.ResumeRound);
		Assert.Equal(stopped.Version + 1, stored.Version);
		Assert.Equal(stopped.UpdatedUtc, stored.UpdatedUtc);
	}

	[Fact]
	public async Task RetryAsync_RefusesACardThatAPersonEditedAfterTheReadSoTheEditedTaskIsNotResumedMidWay()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: RoundKind.Review);
		CardService service = new(database.ContextFactory, database.Clock);
		CardService otherPerson = new(database.ContextFactory, database.Clock);
		CardInput edit = await TestCardInput.CreateAsync(database, "Edited meanwhile");
		interceptor.Arm(async () => ResultAssert.Succeeded(await otherPerson.UpdateAsync(stopped.Id, edit, TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal(Changed, message);
		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal("Edited meanwhile", stored.Name);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(3, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}

	[Theory]
	[InlineData("\"State\" = 0", "The card was made Ready by something that did not touch its version.")]
	[InlineData("\"Lane\" = 1", "The card was moved to In Progress by something that did not touch its version.")]
	public async Task RetryAsync_WritesOnlyWhileTheCardIsStillTheStoppedCardItChecked_EvenWhenItsVersionDidNotMove(string change, string because)
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: RoundKind.Gate);
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"UPDATE \"Cards\" SET {change} WHERE \"Id\" = {stopped.Id}");

		string message = ResultAssert.Failed(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal(NotStopped, message);
		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal(3, stored.Attempt);
		Assert.Equal(stopped.Version, stored.Version);
		Assert.True(stored.UpdatedUtc == stopped.UpdatedUtc, $"The retry must not have written: {because}");
	}

	[Fact]
	public async Task RetryAsync_WhenAnotherPersonRetriesFirst_RefusesBecauseTheCardIsNoLongerStopped()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3);
		CardService service = new(database.ContextFactory, database.Clock);
		CardService otherPerson = new(database.ContextFactory, database.Clock);
		interceptor.Arm(async () => ResultAssert.Succeeded(await otherPerson.RetryAsync(stopped.Id, TestContext.Current.CancellationToken)));

		string message = ResultAssert.Failed(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal(NotStopped, message);
		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(stopped.Version + 1, stored.Version);
	}

	[Fact]
	public async Task RetryAsync_ReportsACardThatWasDeletedAfterTheReadAsNotFound()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error);
		CardService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm($"DELETE FROM \"Cards\" WHERE \"Id\" = {stopped.Id}");

		string message = ResultAssert.Failed(await service.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("Card not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(database));
	}

	[Theory]
	[InlineData(null, RoundKind.Work)]
	[InlineData(RoundKind.Work, RoundKind.Work)]
	[InlineData(RoundKind.Gate, RoundKind.Gate)]
	public async Task RetryAsync_LetsThePipelineClaimTheCardAgainAtTheStageItStoppedAtOnAFreshFirstAttempt(RoundKind? resumeRound, RoundKind expectedKind)
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		WorkCard stopped = await TestData.AddCardAsync(rig.Database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: resumeRound);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		ResultAssert.Succeeded(await rig.Cards.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));
		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(stopped.Id, work.CardId);
		Assert.Equal(expectedKind, work.Kind);
		Assert.Equal(1, work.Attempt);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal(1, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task RetryAsync_AfterThePipelineBlockedTheCard_LetsItRunAgainAndShowsItReadyOnTheBoard()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		await rig.RecordAsync(work, RoundResult.Blocked, "Needs the staging credentials.");
		Assert.Equal(CardState.Error, (await TestData.FindCardAsync(rig.Database, card.Id))?.State);

		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));

		Assert.Equal(CardState.Ready, (await TestData.FindCardAsync(rig.Database, card.Id))?.State);
		PipelineWork again = await rig.ClaimAsync();
		Assert.Equal(card.Id, again.CardId);
		Assert.Equal(RoundKind.Work, again.Kind);
		Assert.Equal(2, again.Sequence);
	}
}
