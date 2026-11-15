using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A free worker takes the card at the top of the Queue; nothing else is claimed unless it was interrupted.</summary>
public sealed class CardPipelineServiceClaimTests
{
	[Fact]
	public async Task ClaimNextAsync_TakesTheTopOfTheQueueAndMovesItToTheBottomOfInProgress()
	{
		await using PipelineRig rig = new();
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B", "C");
		await TestData.AddLaneAsync(rig.Database, CardLane.InProgress, "Busy");

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(queue["A"].Id, work.CardId);
		Assert.Equal("Busy,A", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("B,C", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Queue));
	}

	[Fact]
	public async Task ClaimNextAsync_OpensWorkRoundOneWithTheWorkersSnapshotAndABriefing()
	{
		await using PipelineRig rig = new();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false, HarnessKind.Codex, "codex.exe", "--full-auto");
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Fix the login", workerAgentId: worker.Id, description: "Plus signs are rejected.");
		DateTime claimedAt = rig.Database.Clock.Tick();

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(card.Id, work.CardId);
		Assert.Equal(1, work.Sequence);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(1, work.Attempt);
		Assert.Equal("Fix the login", work.CardName);
		Assert.NotNull(work.Agent);
		Assert.Equal("Wanda", work.Agent.Name);
		Assert.Equal(HarnessKind.Codex, work.Agent.Harness);
		Assert.Equal("codex.exe", work.Agent.Command);
		Assert.Equal("--full-auto", work.Agent.Arguments);
		Assert.NotNull(work.Briefing);
		Assert.Contains($"# Card {card.Id}: Fix the login", work.Briefing, StringComparison.Ordinal);
		Assert.Contains("Plus signs are rejected.", work.Briefing, StringComparison.Ordinal);
		Assert.Contains("- Stage: Work (attempt 1 of 5; round 1 of this card)", work.Briefing, StringComparison.Ordinal);
		Assert.Contains($"`{PipelineContract.BranchName(card.Id)}`", work.Briefing, StringComparison.Ordinal);
		Assert.Contains("started from `main`", work.Briefing, StringComparison.Ordinal);

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Equal(work.RoundId, stored.Id);
		Assert.Equal(1, stored.Sequence);
		Assert.Equal(1, stored.Attempt);
		Assert.Equal(RoundKind.Work, stored.Kind);
		Assert.Null(stored.Outcome);
		Assert.Null(stored.EndedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.StartedUtc.Kind);
		Assert.Equal(claimedAt, stored.StartedUtc);
		Assert.Equal(work.Briefing, stored.Briefing);
		Assert.NotNull(stored.Agent);
		Assert.Equal("Wanda", stored.Agent.Name);
		Assert.Equal(HarnessKind.Codex, stored.Agent.Harness);
		Assert.Equal("codex.exe", stored.Agent.Command);
		Assert.Equal("--full-auto", stored.Agent.Arguments);
	}

	[Fact]
	public async Task ClaimNextAsync_AdvancesTheCardVersionAndStampsTheChange()
	{
		await using PipelineRig rig = new();
		DateTime arranged = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", stampedUtc: arranged);
		DateTime claimedAt = rig.Database.Clock.Tick();

		await rig.ClaimAsync();

		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(1, stored.Attempt);
		Assert.Equal(card.Version + 1, stored.Version);
		Assert.Equal(claimedAt, stored.UpdatedUtc);
		Assert.Equal(arranged, stored.CreatedUtc);
	}

	[Fact]
	public async Task ClaimNextAsync_TakesCardsFirstInFirstOutAndThenFindsNothing()
	{
		await using PipelineRig rig = new();
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B", "C");

		PipelineWork first = await rig.ClaimAsync();
		PipelineWork second = await rig.ClaimAsync();
		PipelineWork third = await rig.ClaimAsync();

		int[] expectedOrder = [queue["A"].Id, queue["B"].Id, queue["C"].Id];
		int[] claimedOrder = [first.CardId, second.CardId, third.CardId];
		Assert.Equal(expectedOrder, claimedOrder);
		Assert.Equal("A,B,C", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.InProgress));
		Assert.Empty(await TestData.ReadLaneAsync(rig.Database, CardLane.Queue));
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ClaimNextAsync_FollowsTheQueueOrderRatherThanTheOrderCardsWereCreated()
	{
		await using PipelineRig rig = new();
		Dictionary<string, WorkCard> queue = await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "A", "B");
		await using (ProjectDbContext context = rig.Database.CreateContext())
		{
			await context.Cards.Where(card => card.Id == queue["A"].Id).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.SortOrder, 5), TestContext.Current.CancellationToken);
		}

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(queue["B"].Id, work.CardId);
	}

	[Fact]
	public async Task ClaimNextAsync_OfAnEmptyQueue_ReturnsNullAndChangesNothing()
	{
		await using PipelineRig rig = new();

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		Assert.Equal(0, await TestData.CountRoundsAsync(rig.Database));
	}

	[Theory]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	[InlineData(CardLane.Complete)]
	[InlineData(CardLane.Closed)]
	public async Task ClaimNextAsync_NeverClaimsACardOutsideTheQueueThatWasNotInterrupted(CardLane lane)
	{
		await using PipelineRig rig = new();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Elsewhere", lane);

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		await CardAssert.UnchangedAsync(rig.Database, card, expectedCardCount: 1);
		Assert.Equal(0, await TestData.CountRoundsAsync(rig.Database));
	}

	[Fact]
	public async Task ClaimNextAsync_WithoutAConnectedRepository_ReturnsNullAndChangesNothing()
	{
		await using PipelineRig rig = new();
		await rig.RemoveRepositoryAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Waiting");

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		await CardAssert.UnchangedAsync(rig.Database, card, expectedCardCount: 1);
		Assert.Equal(0, await TestData.CountRoundsAsync(rig.Database));
	}

	[Fact]
	public async Task ClaimNextAsync_AfterTheRepositoryIsConnected_ClaimsTheCardThatWaited()
	{
		await using PipelineRig rig = new();
		await rig.RemoveRepositoryAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Waiting");
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		TestData.ConnectRepository(rig.Database, targetBranch: "develop");
		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(card.Id, work.CardId);
		Assert.NotNull(work.Briefing);
		Assert.Contains("started from `develop`", work.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ClaimNextAsync_PassesOverACardWithoutAWorkerAndClaimsTheNextOne()
	{
		await using PipelineRig rig = new();
		WorkCard nobody = await TestData.AddCardAsync(rig.Database, "Nobody assigned", workerAgentId: null);
		WorkCard next = await TestData.AddCardAsync(rig.Database, "Next");

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(next.Id, work.CardId);
		WorkCard? stopped = await TestData.FindCardAsync(rig.Database, nobody.Id);
		Assert.NotNull(stopped);
		Assert.Equal(CardState.Error, stopped.State);
		Assert.Equal(CardLane.Queue, stopped.Lane);
		CardRound blocked = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, nobody.Id));
		Assert.Equal(RoundKind.Work, blocked.Kind);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal("No worker is assigned.", blocked.Note);
		Assert.Null(blocked.Agent);
		Assert.Null(blocked.Briefing);
	}

	[Fact]
	public async Task ClaimNextAsync_GivesEachCardItsOwnWorkerNotTheReviewer()
	{
		await using PipelineRig rig = new();
		Agent wanda = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: true);
		Agent walt = await TestData.AddAgentAsync(rig.Database, "Walt", canWork: true, canReview: false);
		Agent rita = await TestData.AddAgentAsync(rig.Database, "Rita", canWork: false, canReview: true);
		await TestData.AddCardAsync(rig.Database, "First", workerAgentId: wanda.Id, reviewerAgentId: rita.Id);
		await TestData.AddCardAsync(rig.Database, "Second", workerAgentId: walt.Id, reviewerAgentId: wanda.Id);

		PipelineWork first = await rig.ClaimAsync();
		PipelineWork second = await rig.ClaimAsync();

		Assert.Equal("Wanda", first.Agent?.Name);
		Assert.Equal("Walt", second.Agent?.Name);
	}
}
