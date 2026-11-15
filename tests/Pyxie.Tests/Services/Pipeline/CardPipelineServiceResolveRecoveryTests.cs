using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A resolve round that was interrupted, or was running when Pyxie stopped, reopens as a resolve round on the same attempt, or as a work round when the project no longer has a merge agent. A running resolve round keeps the merge agent it started with, and the merge agent can be removed or reconfigured at any time.</summary>
public sealed class CardPipelineServiceResolveRecoveryTests
{
	private const string Stopped = "The pipeline was stopped.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<PipelineRig> CreateRigAsync()
	{
		PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await rig.AddMergerAsync();

		return rig;
	}

	private static async Task<PipelineWork> ClaimResolveAsync(PipelineRig rig)
	{
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge), "Conflict in src/Login.cs", "CONFLICTREPORT");
		Assert.Equal(RoundKind.Resolve, resolve.Kind);

		return resolve;
	}

	private static async Task<Agent> MergeAgentAsync(PipelineRig rig)
	{
		return Assert.Single(await rig.Agents.ListAsync(Token), agent => agent.CanMerge);
	}

	[Fact]
	public async Task InterruptAsync_OfAResolveRound_ClosesItAsInterruptedAndLeavesTheCardInProgressOnTheSameAttempt()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		await TestData.AddCardAsync(rig.Database, "Behind", CardLane.InProgress);
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(resolve.RoundId, Stopped, Token));

		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(before.SortOrder, stored.SortOrder);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(2, stored.Attempt);
		CardRound interrupted = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(resolve.RoundId, interrupted.Id);
		Assert.Equal(RoundKind.Resolve, interrupted.Kind);
		Assert.Equal(RoundOutcome.Interrupted, interrupted.Outcome);
		Assert.Equal(Stopped, interrupted.Note);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAnInterruptedResolveRound_ResumesTheResolveRoundOnTheSameAttemptWithTheMergeAgentAndSaysHowTheLastAttemptEnded()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(resolve.RoundId, Stopped, Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(card.Id, resumed.CardId);
		Assert.Equal(RoundKind.Resolve, resumed.Kind);
		Assert.Equal(2, resumed.Attempt);
		Assert.Equal(resolve.Sequence + 1, resumed.Sequence);
		Assert.NotEqual(resolve.RoundId, resumed.RoundId);
		Assert.Equal(PipelineRig.MergerName, resumed.Agent?.Name);
		Assert.NotNull(resumed.Briefing);
		string previous = BriefingSections.Get(resumed.Briefing, "How the previous attempt ended");
		Assert.Contains("Round 5, attempt 2, Resolve, Interrupted by Merger.", previous, StringComparison.Ordinal);
		Assert.Contains(Stopped, previous, StringComparison.Ordinal);
		Assert.Contains("CONFLICTREPORT", BriefingSections.Get(resumed.Briefing, "Why the merge stopped"), StringComparison.Ordinal);
		Assert.Contains("- Stage: Resolve (attempt 2 of 5; round 6 of this card)", resumed.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Interrupted", "Resolve@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(3));
		Assert.Equal(CardLane.InProgress, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterPyxieStoppedDuringAResolveRound_ResumesTheResolveRoundOnTheSameAttempt()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		Assert.Equal(1, await rig.Pipeline.RecoverInterruptedAsync(Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Resolve, resumed.Kind);
		Assert.Equal(2, resumed.Attempt);
		Assert.Equal(resolve.Sequence + 1, resumed.Sequence);
		Assert.Equal(PipelineRig.MergerName, resumed.Agent?.Name);
		Assert.NotNull(resumed.Briefing);
		Assert.Contains("Pyxie stopped while this round was running.", BriefingSections.Get(resumed.Briefing, "How the previous attempt ended"), StringComparison.Ordinal);
		Assert.Equal(["Resolve@2:Interrupted", "Resolve@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ClaimNextAsync_AfterAnInterruptedResolveRoundAndTheMergeAgentWasRemoved_ReopensItAsAWorkRoundForTheWorkerOnTheSameAttempt(bool delete)
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(resolve.RoundId, Stopped, Token));
		Agent merger = await MergeAgentAsync(rig);
		if (delete)
		{
			ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, Token));
		}
		else
		{
			ResultAssert.Succeeded(await rig.Agents.UpdateAsync(merger.Id, new AgentInput(merger.Name, merger.Harness, merger.Command, merger.Arguments, CanWork: false, CanReview: false, CanMerge: false), Token));
		}

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Work, resumed.Kind);
		Assert.Equal(2, resumed.Attempt);
		Assert.Equal(resolve.Sequence + 1, resumed.Sequence);
		Assert.Equal(TestData.DefaultAgentName, resumed.Agent?.Name);
		Assert.NotNull(resumed.Briefing);
		Assert.Contains("- Stage: Work (attempt 2 of 5; round 6 of this card)", resumed.Briefing, StringComparison.Ordinal);
		Assert.Contains("CONFLICTREPORT", BriefingSections.Get(resumed.Briefing, "Why this card came back"), StringComparison.Ordinal);
		Assert.Contains("`git merge main`", resumed.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Resolve@2:Interrupted", "Work@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal(CardState.Ready, stored.State);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAnInterruptedResolveRoundAndTheMergeAgentWasReconfigured_ReopensItWithTheAgentAsItIsNowWhileTheInterruptedRoundKeepsItsOwn()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(resolve.RoundId, Stopped, Token));
		Agent merger = await MergeAgentAsync(rig);
		ResultAssert.Succeeded(await rig.Agents.UpdateAsync(merger.Id, new AgentInput("Resolver", HarnessKind.ClaudeCode, "claude", "--print", CanWork: false, CanReview: false, CanMerge: true), Token));

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Resolve, resumed.Kind);
		Assert.Equal("Resolver", resumed.Agent?.Name);
		Assert.Equal("--print", resumed.Agent?.Arguments);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(PipelineRig.MergerName, rounds[^2].Agent?.Name);
		Assert.Equal("exec --full-auto", rounds[^2].Agent?.Arguments);
		Assert.Equal("Resolver", rounds[^1].Agent?.Name);
	}

	[Fact]
	public async Task ClaimNextAsync_AfterAnInterruptedResolveRoundAndAnotherAgentBecameTheMergeAgent_ReopensItWithTheNewMergeAgent()
	{
		await using PipelineRig rig = await CreateRigAsync();
		await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		ResultAssert.Succeeded(await rig.Pipeline.InterruptAsync(resolve.RoundId, Stopped, Token));
		Agent merger = await MergeAgentAsync(rig);
		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, Token));
		await TestData.AddAgentAsync(rig.Database, "Newcomer", canWork: false, canReview: false, HarnessKind.OpenCode, "opencode", canMerge: true);

		PipelineWork resumed = await rig.ClaimAsync();

		Assert.Equal(RoundKind.Resolve, resumed.Kind);
		Assert.Equal("Newcomer", resumed.Agent?.Name);
		Assert.Equal(HarnessKind.OpenCode, resumed.Agent?.Harness);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task RecordResultAsync_OfARunningResolveRoundWhoseMergeAgentWasRemoved_IsAcceptedAndTheRoundKeepsTheMergeAgentItStartedWith(bool delete)
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		Agent merger = await MergeAgentAsync(rig);
		if (delete)
		{
			ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, Token));
		}
		else
		{
			ResultAssert.Succeeded(await rig.Agents.UpdateAsync(merger.Id, new AgentInput("Renamed", HarnessKind.Other, "other", string.Empty, CanWork: false, CanReview: false, CanMerge: false), Token));
		}

		PipelineWork? gate = await rig.RecordAsync(resolve, RoundResult.Succeeded, "Combined both", "RESOLUTIONREPORT");

		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		CardRound finished = rounds.Single(round => round.Id == resolve.RoundId);
		Assert.Equal(RoundOutcome.Resolved, finished.Outcome);
		Assert.Equal(PipelineRig.MergerName, finished.Agent?.Name);
		Assert.Equal("codex", finished.Agent?.Command);
	}

	[Fact]
	public async Task RecordResultAsync_OfAResolveRoundThatIsErroredAfterTheMergeAgentWasRemoved_FallsBackToTheWorkerForTheNextAttempt()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		Agent merger = await MergeAgentAsync(rig);
		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, Token));

		PipelineWork? next = await rig.RecordAsync(resolve, RoundResult.Errored, "The merge agent crashed");

		Assert.NotNull(next);
		Assert.Equal(RoundKind.Work, next.Kind);
		Assert.Equal(3, next.Attempt);
		Assert.Equal(TestData.DefaultAgentName, next.Agent?.Name);
		Assert.NotNull(next.Briefing);
		Assert.Contains("`git merge main`", next.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Errored", "Work@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(3));
	}

	[Fact]
	public async Task RecordResultAsync_OfAResolveRoundBlockedAfterTheMergeAgentWasRemoved_StopsTheCardAtTheResolveRoundBecauseTheRoundThatBlockedWasOne()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await ClaimResolveAsync(rig);
		Agent merger = await MergeAgentAsync(rig);
		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, Token));

		Assert.Null(await rig.RecordAsync(resolve, RoundResult.Blocked, "Needs a person"));

		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(RoundKind.Resolve, stored.ResumeRound);
	}
}
