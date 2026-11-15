using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A conflict, an error, or a block in a resolve round uses attempts and stops the card like any other round: the card waits in Error at the top of the Queue until a person retries it, and the retry resumes at the resolve round, or at work when the project no longer has a merge agent. A resolve round cannot fail.</summary>
public sealed class CardPipelineServiceResolveStopTests
{
	private const string NoteRequired = "A note is required when a round fails, is rejected, conflicts, errors, or is blocked.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<PipelineRig> CreateRigAsync()
	{
		PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await rig.AddMergerAsync();

		return rig;
	}

	private static async Task<PipelineWork> ClaimResolveAsync(PipelineRig rig, string conflictNote = "Conflict in src/Login.cs", string? conflictReport = "CONFLICTREPORT")
	{
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge), conflictNote, conflictReport);
		Assert.Equal(RoundKind.Resolve, resolve.Kind);

		return resolve;
	}

	private static async Task RemoveMergeAgentAsync(PipelineRig rig, bool delete)
	{
		Agent merger = Assert.Single(await rig.Agents.ListAsync(Token), agent => agent.CanMerge);
		if (delete)
		{
			ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, Token));
		}
		else
		{
			ResultAssert.Succeeded(await rig.Agents.UpdateAsync(merger.Id, new AgentInput(merger.Name, merger.Harness, merger.Command, merger.Arguments, merger.CanWork, merger.CanReview, CanMerge: false), Token));
		}
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictOnTheLastAttemptWithAMergeAgent_StopsTheCardAtTheResolveRoundAtTheTopOfTheQueue()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 1);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "Waiting");

		PipelineWork? next = await rig.RecordAsync(merge, RoundResult.Failed, "Conflict in src/Login.cs", "CONFLICTREPORT");

		Assert.Null(next);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(0, stored.SortOrder);
		Assert.Equal(1, stored.Attempt);
		Assert.Equal(RoundKind.Resolve, stored.ResumeRound);
		Assert.Equal("Card,Waiting", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(["Work@1:Completed", "Gate@1:Passed", "Review@1:Approved", "Merge@1:Conflicted"], rounds.Select(PipelineRoundLog.Describe));
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
		Assert.Equal("Waiting", (await rig.ClaimAsync()).CardName);
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictOnTheLastAttemptWithoutAMergeAgent_StillStopsTheCardAtWork()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 1);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);

		Assert.Null(await rig.RecordAsync(merge, RoundResult.Failed, "Conflict in src/Login.cs"));

		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task RecordResultAsync_OfAnErroredResolveRound_OpensAnotherResolveRoundOnTheNextAttemptThatSaysHowTheLastOneEnded()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);

		PipelineWork? again = await rig.RecordAsync(resolve, RoundResult.Errored, "The merge agent crashed", "PARTIALWORK");

		Assert.NotNull(again);
		Assert.Equal(RoundKind.Resolve, again.Kind);
		Assert.Equal(3, again.Attempt);
		Assert.Equal(PipelineRig.MergerName, again.Agent?.Name);
		Assert.NotNull(again.Briefing);
		string previous = BriefingSections.Get(again.Briefing, "How the previous attempt ended");
		Assert.Contains("Round 5, attempt 2, Resolve, Errored by Merger.", previous, StringComparison.Ordinal);
		Assert.Contains("The merge agent crashed", previous, StringComparison.Ordinal);
		Assert.Contains("PARTIALWORK", previous, StringComparison.Ordinal);
		Assert.Contains("CONFLICTREPORT", BriefingSections.Get(again.Briefing, "Why the merge stopped"), StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Errored", "Resolve@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(3));
		Assert.Equal(CardLane.InProgress, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
	}

	[Fact]
	public async Task RecordResultAsync_OfAnErroredResolveRoundOnTheLastAttempt_StopsTheCardAtTheResolveRound()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 2);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		Assert.Equal(2, resolve.Attempt);

		PipelineWork? next = await rig.RecordAsync(resolve, RoundResult.Errored, "The merge agent crashed");

		Assert.Null(next);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal(RoundKind.Resolve, stored.ResumeRound);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Errored"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		Assert.Null(await rig.Pipeline.ClaimNextAsync(Token));
	}

	[Fact]
	public async Task RecordResultAsync_OfABlockedResolveRound_StopsTheCardAtOnceAtTheResolveRoundOnTheSameAttempt()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		await TestData.AddLaneAsync(rig.Database, CardLane.Queue, "Waiting");

		PipelineWork? next = await rig.RecordAsync(resolve, RoundResult.Blocked, "The two changes cannot both hold");

		Assert.Null(next);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(0, stored.SortOrder);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal(RoundKind.Resolve, stored.ResumeRound);
		Assert.Equal("Card,Waiting", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Queue));
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Blocked"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAStoppedResolveRoundIsRetried_OpensTheResolveRoundOnAFreshFirstAttemptWithTheMergeAgentAndTheBlockedNote()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		await rig.RecordAsync(resolve, RoundResult.Blocked, "Needs a person to say which side wins");
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(card.Id, resumed.CardId);
		Assert.Equal(RoundKind.Resolve, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
		Assert.Equal(resolve.Sequence + 1, resumed.Sequence);
		Assert.Equal(PipelineRig.MergerName, resumed.Agent?.Name);
		Assert.NotNull(resumed.Briefing);
		Assert.Contains("Needs a person to say which side wins", BriefingSections.Get(resumed.Briefing, "How the previous attempt ended"), StringComparison.Ordinal);
		Assert.Contains("CONFLICTREPORT", BriefingSections.Get(resumed.Briefing, "Why the merge stopped"), StringComparison.Ordinal);
		Assert.Contains("- Stage: Resolve (attempt 1 of 5; round 6 of this card)", resumed.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Resolve@2:Blocked", "Resolve@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(1, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterACardStoppedByAConflictOnItsLastAttemptIsRetried_OpensTheResolveRoundOnAFreshFirstAttempt()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 1);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		Assert.Null(await rig.RecordAsync(merge, RoundResult.Failed, "Conflict in src/Login.cs", "CONFLICTREPORT"));
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Resolve, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
		Assert.Equal(PipelineRig.MergerName, resumed.Agent?.Name);
		Assert.NotNull(resumed.Briefing);
		Assert.Contains("CONFLICTREPORT", BriefingSections.Get(resumed.Briefing, "Why the merge stopped"), StringComparison.Ordinal);
		Assert.DoesNotContain("## How the previous attempt ended", resumed.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ClaimNextAsync_AfterAStoppedResolveRoundIsRetriedAndTheMergeAgentWasRemoved_OpensAWorkRoundForTheWorkerWhoMergesTheTargetItself(bool delete)
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		await rig.RecordAsync(resolve, RoundResult.Blocked, "Needs a person");
		await RemoveMergeAgentAsync(rig, delete);
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Work, resumed.Kind);
		Assert.Equal(1, resumed.Attempt);
		Assert.Equal(TestData.DefaultAgentName, resumed.Agent?.Name);
		Assert.NotNull(resumed.Briefing);
		Assert.Contains("CONFLICTREPORT", BriefingSections.Get(resumed.Briefing, "Why this card came back"), StringComparison.Ordinal);
		Assert.Contains("`git merge main`", resumed.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Resolve@2:Blocked", "Work@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Null(stored.ResumeRound);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAStoppedResolveRoundWasEditedAndRetried_StartsTheEditedTaskAtWorkNotAtTheResolveRound()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		await rig.RecordAsync(resolve, RoundResult.Blocked, "Needs a person");
		CardInput edited = await TestCardInput.CreateAsync(rig.Database, "Card, reworded", description: "A different task.", reviewerAgentId: TestData.DefaultAgent);
		ResultAssert.Succeeded(await rig.Cards.UpdateAsync(card.Id, edited, Token));
		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, Token));

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(1, work.Attempt);
		Assert.Equal(["Resolve@2:Blocked", "Work@1:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictWithoutARepository_RecordsTheResolveRoundAsBlockedAndStopsTheCardAtIt()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await rig.RemoveRepositoryAsync();

		PipelineWork? next = await rig.RecordAsync(merge, RoundResult.Failed, "Conflict in src/Login.cs");

		Assert.Null(next);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		CardRound blocked = rounds[^1];
		Assert.Equal(RoundKind.Resolve, blocked.Kind);
		Assert.Equal(RoundOutcome.Blocked, blocked.Outcome);
		Assert.Equal("No repository is connected.", blocked.Note);
		Assert.Null(blocked.Agent);
		Assert.Null(blocked.Briefing);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(RoundKind.Resolve, stored.ResumeRound);
	}

	[Theory]
	[InlineData(RoundResult.Failed, "A resolve round cannot fail. Report Errored or Blocked with the reason instead.")]
	[InlineData(RoundResult.Outdated, "Only a merge round can be outdated.")]
	[InlineData((RoundResult)99, "Result not recognized.")]
	public async Task RecordResultAsync_OfAResolveRoundThatCannotEndThatWay_IsRefusedWithTheReasonAndTheRoundStaysOpen(RoundResult result, string expected)
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(resolve.RoundId, result, new RoundReport("Could not finish", null), Token));

		Assert.Equal(expected, message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		Assert.Equal("Resolve@2:Open", (await PipelineRoundLog.ReadAsync(rig.Database, card.Id))[^1]);
		PipelineWork? gate = await rig.RecordAsync(resolve, RoundResult.Succeeded);
		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
	}

	[Theory]
	[InlineData(RoundResult.Errored)]
	[InlineData(RoundResult.Blocked)]
	public async Task RecordResultAsync_OfAnErroredOrBlockedResolveRoundWithoutANote_IsRefusedAndTheRoundStaysOpen(RoundResult result)
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(resolve.RoundId, result, new RoundReport("  ", "Some report"), Token));

		Assert.Equal(NoteRequired, message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
	}

	[Fact]
	public async Task RecordResultAsync_OfAResolvedResolveRoundWithoutANote_IsAccepted()
	{
		await using PipelineRig rig = await CreateRigAsync();
		await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);

		PipelineWork? gate = await rig.RecordAsync(resolve, RoundResult.Succeeded);

		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
	}
}
