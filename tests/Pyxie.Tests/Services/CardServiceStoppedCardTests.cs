using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A card the pipeline stopped waits in the Queue, where people can still edit or delete it; editing it keeps it stopped and makes its next run start at work.</summary>
public sealed class CardServiceStoppedCardTests
{
	[Fact]
	public async Task UpdateAsync_OfAStoppedCard_KeepsItStoppedClearsItsResumeStageAndLeavesItsAttemptsAlone()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3, attemptLimit: 3, resumeRound: RoundKind.Review);
		CardService service = new(database.ContextFactory, database.Clock);
		DateTime editedAt = database.Clock.Tick();

		CardInput edit = await TestCardInput.CreateAsync(database, "Reworded", PyxieSeedData.BugCategoryId, description: "A clearer task", attemptLimit: 7);

		WorkCard updated = ResultAssert.Succeeded(await service.UpdateAsync(stopped.Id, edit, TestContext.Current.CancellationToken));

		Assert.Equal(CardState.Error, updated.State);
		Assert.Null(updated.ResumeRound);
		WorkCard? stored = await TestData.FindCardAsync(database, stopped.Id);
		Assert.NotNull(stored);
		Assert.Equal("Reworded", stored.Name);
		Assert.Equal("A clearer task", stored.Description);
		Assert.Equal(PyxieSeedData.BugCategoryId, stored.CategoryId);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(3, stored.Attempt);
		Assert.Equal(7, stored.AttemptLimit);
		Assert.Null(stored.ResumeRound);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(0, stored.SortOrder);
		Assert.Equal(stopped.Version + 1, stored.Version);
		Assert.Equal(editedAt, stored.UpdatedUtc);
	}

	[Fact]
	public async Task UpdateAsync_OfAReadyCardInTheQueue_LeavesItsStateAndAttemptsAlone()
	{
		await using TestDatabase database = new();
		WorkCard ready = await TestData.AddCardAsync(database, "Ready");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(ready.Id, await TestCardInput.CreateAsync(database, "Reworded"), TestContext.Current.CancellationToken));

		WorkCard? stored = await TestData.FindCardAsync(database, ready.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(0, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task UpdateAsync_ThenRetry_MakesThePipelineStartTheEditedCardAtWorkNotWhereItStopped()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		WorkCard stopped = await TestData.AddCardAsync(rig.Database, "Stopped", state: CardState.Error, attempt: 3, resumeRound: RoundKind.Gate);
		ResultAssert.Succeeded(await rig.Cards.UpdateAsync(stopped.Id, await TestCardInput.CreateAsync(rig.Database, "Stopped, reworded"), TestContext.Current.CancellationToken));
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		ResultAssert.Succeeded(await rig.Cards.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));
		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(stopped.Id, work.CardId);
	}

	[Fact]
	public async Task DeleteAsync_OfAStoppedCard_RemovesItWithItsRoundsAndResequencesTheQueue()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 3);
		WorkCard keeper = await TestData.AddCardAsync(database, "Keeper");
		WorkCard other = await TestData.AddCardAsync(database, "Other");
		await TestData.AddRoundAsync(database, stopped.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, stopped.Id, 2, RoundKind.Gate, RoundOutcome.Failed, note: "Build failed");
		await TestData.AddRoundAsync(database, keeper.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		CardService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.DeleteAsync(stopped.Id, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindCardAsync(database, stopped.Id));
		Assert.Empty(await TestData.ReadRoundsAsync(database, stopped.Id));
		Assert.Single(await TestData.ReadRoundsAsync(database, keeper.Id));
		Assert.Equal(1, await TestData.CountRoundsAsync(database));
		Assert.Equal("Keeper,Other", await TestData.ReadLaneNamesAsync(database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(database, CardLane.Queue));
		Assert.Equal(other.Version, (await TestData.FindCardAsync(database, other.Id))?.Version);
	}

	[Fact]
	public async Task GetHistoryAsync_OfAStoppedCard_StillShowsEveryRoundItRan()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 1);
		await TestData.AddRoundAsync(database, stopped.Id, 1, RoundKind.Work, RoundOutcome.Blocked, "Wanda", note: "Needs the API key.");
		CardService service = new(database.ContextFactory, database.Clock);

		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await service.GetHistoryAsync(stopped.Id, TestContext.Current.CancellationToken));

		CardRound blocked = Assert.Single(history);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal("Needs the API key.", blocked.Note);
	}
}
