using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>Several pipeline workers claim at once and people edit or delete cards meanwhile. Each race is staged at the moment between the claim's read and its conditional write, by running the competing operation to completion there: the claim then finds its read stale, re-reads, and acts only on the state that is really stored.</summary>
public sealed class CardPipelineServiceClaimRaceTests
{
	[Fact]
	public async Task ClaimNextAsync_WhenAnotherWorkerClaimsTheSameCardFirst_ReadsAgainAndClaimsTheNextCard()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B", "C");
		CardPipelineService otherWorker = rig.CreateSecondPipeline();
		PipelineWork? stolen = null;
		interceptor.Arm(async () => stolen = await otherWorker.ClaimNextAsync(TestContext.Current.CancellationToken));

		PipelineWork mine = await rig.ClaimAsync();

		interceptor.AssertFired();
		Assert.NotNull(stolen);
		Assert.Equal(queue["A"].Id, stolen.CardId);
		Assert.Equal(queue["B"].Id, mine.CardId);
		Assert.Equal("A,B", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("C", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, queue["A"].Id));
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, queue["B"].Id));
		Assert.Equal(2, await TestData.CountRoundsAsync(rig.Database));
	}

	[Fact]
	public async Task ClaimNextAsync_WhenAnotherWorkerTakesTheOnlyCard_FindsNothingAndOpensNoSecondRound()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard only = await TestData.AddCardAsync(rig.Database, "Only");
		CardPipelineService otherWorker = rig.CreateSecondPipeline();
		interceptor.Arm(async () => await otherWorker.ClaimNextAsync(TestContext.Current.CancellationToken));

		PipelineWork? mine = await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);

		interceptor.AssertFired();
		Assert.Null(mine);
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, only.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, only.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(1, stored.Version);
	}

	[Fact]
	public async Task ClaimNextAsync_WhenTheCardIsMovedAwayAfterTheRead_NeverClaimsItFromTheStaleRead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B");
		interceptor.Arm($"UPDATE \"Cards\" SET \"Lane\" = {(int)CardLane.Complete}, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {queue["A"].Id}");

		PipelineWork mine = await rig.ClaimAsync();

		interceptor.AssertFired();
		Assert.Equal(queue["B"].Id, mine.CardId);
		WorkCard? moved = await TestData.FindCardAsync(rig.Database, queue["A"].Id);
		Assert.NotNull(moved);
		Assert.Equal(CardLane.Complete, moved.Lane);
		Assert.Equal(1, moved.Version);
		Assert.Empty(await TestData.ReadRoundsAsync(rig.Database, queue["A"].Id));
		Assert.Equal("B", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
	}

	[Fact]
	public async Task ClaimNextAsync_WhenAPersonDeletesTheTopCardAfterTheRead_ClaimsTheNextCardInstead()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B");
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.DeleteAsync(queue["A"].Id, TestContext.Current.CancellationToken)));

		PipelineWork mine = await rig.ClaimAsync();

		interceptor.AssertFired();
		Assert.Equal(queue["B"].Id, mine.CardId);
		Assert.Null(await TestData.FindCardAsync(rig.Database, queue["A"].Id));
		Assert.Equal(1, await TestData.CountCardsAsync(rig.Database));
		Assert.Equal(1, await TestData.CountRoundsAsync(rig.Database));
		Assert.Empty(await TestData.ReadLaneAsync(rig.Database, CardLane.Queue));
	}

	[Fact]
	public async Task ClaimNextAsync_WhenAPersonEditsTheTopCardAfterTheRead_ClaimsItWithTheEditedFieldsAndOneRound()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard card = await rig.CreateCardAsync("Original", description: "Original text");
		CardInput edit = await TestCardInput.CreateAsync(rig.Database, "Renamed", description: "New text");
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, edit, TestContext.Current.CancellationToken)));

		PipelineWork mine = await rig.ClaimAsync();

		interceptor.AssertFired();
		Assert.Equal(card.Id, mine.CardId);
		Assert.NotNull(mine.Briefing);
		Assert.Contains("# Card " + card.Id + ": Renamed", mine.Briefing, StringComparison.Ordinal);
		Assert.Contains("New text", mine.Briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("Original text", mine.Briefing, StringComparison.Ordinal);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal("Renamed", stored.Name);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(2, stored.Version);
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task ClaimNextAsync_WhenAPersonChangesTheWorkerAfterTheRead_SnapshotsTheNewWorker()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent oldWorker = await TestData.AddAgentAsync(rig.Database, "Old worker", canWork: true, canReview: false);
		Agent newWorker = await TestData.AddAgentAsync(rig.Database, "New worker", canWork: true, canReview: false);
		WorkCard card = await rig.CreateCardAsync("Card", oldWorker.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, TestCardInput.Create("Card", workerAgentId: newWorker.Id), TestContext.Current.CancellationToken)));

		PipelineWork mine = await rig.ClaimAsync();

		interceptor.AssertFired();
		Assert.Equal("New worker", mine.Agent?.Name);
		Assert.Equal("New worker", (await TestData.ReadRoundsAsync(rig.Database, card.Id))[0].Agent?.Name);
	}

	[Fact]
	public async Task ClaimNextAsync_ByManyWorkersInTurn_NeverHandsOutACardTwice()
	{
		await using PipelineRig rig = new();
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B", "C", "D", "E");
		CardPipelineService[] workers = [rig.Pipeline, rig.CreateSecondPipeline(), rig.CreateSecondPipeline()];
		List<int> claimed = [];

		for (int turn = 0; turn < 6; turn++)
		{
			PipelineWork? work = await workers[turn % workers.Length].ClaimNextAsync(TestContext.Current.CancellationToken);
			if (work is not null)
			{
				claimed.Add(work.CardId);
			}
		}

		Assert.Equal(queue.Values.Select(card => card.Id).Order(), claimed.Order());
		Assert.Equal(5, await TestData.CountRoundsAsync(rig.Database));
	}
}
