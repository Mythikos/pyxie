using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A conflicted merge goes to the project's merge agent as a resolve round in In Progress, which uses an attempt; the resolution then goes through the gates and the review again before Pyxie merges. Without a merge agent the conflict goes back to the worker.</summary>
public sealed class CardPipelineServiceResolveTests
{
	private const string ConflictNote = "Conflicts in src/Login.cs and src/Auth.cs.";
	private const string ConflictReport = "CONFLICT (content): Merge conflict in src/Login.cs\nCONFLICT (content): Merge conflict in src/Auth.cs";

	private static async Task<PipelineRig> CreateRigAsync()
	{
		PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await rig.AddMergerAsync();

		return rig;
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictedMergeWithAMergeAgent_OpensAResolveRoundInProgressOnTheNextAttemptWithTheMergeAgentsSnapshotAndABriefing()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await TestData.AddCardAsync(rig.Database, "Busy", CardLane.InProgress);
		Assert.Equal("Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));

		PipelineWork? resolve = await rig.RecordAsync(merge, RoundResult.Failed, ConflictNote, ConflictReport);

		Assert.NotNull(resolve);
		Assert.Equal(card.Id, resolve.CardId);
		Assert.Equal("Card", resolve.CardName);
		Assert.Equal(RoundKind.Resolve, resolve.Kind);
		Assert.Equal(5, resolve.Sequence);
		Assert.Equal(2, resolve.Attempt);
		AgentSnapshot? agent = resolve.Agent;
		Assert.NotNull(agent);
		Assert.Equal(PipelineRig.MergerName, agent.Name);
		Assert.Equal(HarnessKind.Codex, agent.Harness);
		Assert.Equal("codex", agent.Command);
		Assert.Equal("exec --full-auto", agent.Arguments);
		Assert.NotNull(resolve.Briefing);
		Assert.Contains("- Stage: Resolve (attempt 2 of 5; round 5 of this card)", resolve.Briefing, StringComparison.Ordinal);
		string conflict = BriefingSections.Get(resolve.Briefing, "Why the merge stopped");
		Assert.Contains(ConflictNote, conflict, StringComparison.Ordinal);
		Assert.Contains(ConflictReport, conflict, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		Assert.Equal("Busy,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.InProgress));
		Assert.Equal(string.Empty, await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(2, stored.Attempt);
		Assert.Null(stored.ResumeRound);
		CardRound saved = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(resolve.RoundId, saved.Id);
		Assert.Equal(PipelineRig.MergerName, saved.Agent?.Name);
		Assert.Equal(resolve.Briefing, saved.Briefing);
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictedMergeWithAMergeAgent_GivesTheResolveRoundTheMergeAgentAndNotTheCardsWorkerOrReviewer()
	{
		await using PipelineRig rig = await CreateRigAsync();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false, HarnessKind.OpenCode, "opencode");
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Rita", canWork: false, canReview: true, HarnessKind.Other, "reviewer");
		await rig.CreateCardAsync("Card", worker.Id, reviewer.Id);

		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));

		Assert.Equal(PipelineRig.MergerName, resolve.Agent?.Name);
		Assert.NotEqual(worker.Name, resolve.Agent?.Name);
		Assert.NotEqual(reviewer.Name, resolve.Agent?.Name);
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictedMergeWhoseMergeAgentIsAlsoTheWorker_StillRunsTheResolveRoundWithTheMergeAgent()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		Agent both = await TestData.AddAgentAsync(rig.Database, "Both", canWork: true, canReview: true, canMerge: true);
		await rig.CreateCardAsync("Card", both.Id, both.Id);

		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));

		Assert.Equal(RoundKind.Resolve, resolve.Kind);
		Assert.Equal("Both", resolve.Agent?.Name);
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictedMergeWithoutAMergeAgent_OpensAWorkRoundForTheWorkerNotAResolveRound()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);

		PipelineWork next = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge), ConflictNote, ConflictReport);

		Assert.Equal(RoundKind.Work, next.Kind);
		Assert.Equal(2, next.Attempt);
		Assert.Equal(TestData.DefaultAgentName, next.Agent?.Name);
		Assert.Equal(["Merge@1:Conflicted", "Work@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		Assert.DoesNotContain(await TestData.ReadRoundsAsync(rig.Database, card.Id), round => round.Kind == RoundKind.Resolve);
	}

	[Fact]
	public async Task RecordResultAsync_OfAResolution_OpensTheGatesThenTheReviewThenTheMergeAgainEvenThoughTheWorkWasApprovedBefore()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge), ConflictNote, ConflictReport);

		PipelineWork? gate = await rig.RecordAsync(resolve, RoundResult.Succeeded, "Combined both versions", "RESOLUTIONREPORT");

		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
		Assert.Equal(6, gate.Sequence);
		Assert.Equal(2, gate.Attempt);
		Assert.Null(gate.Agent);
		Assert.Null(gate.Briefing);
		Assert.Equal(CardLane.Gates, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);

		PipelineWork review = await rig.SucceedAsync(gate, "SECONDGATEOUTPUT");

		Assert.Equal(RoundKind.Review, review.Kind);
		Assert.Equal(2, review.Attempt);
		Assert.Equal(TestData.DefaultAgentName, review.Agent?.Name);
		Assert.Equal(CardLane.Review, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
		Assert.NotNull(review.Briefing);
		Assert.Contains("## Merge to review", review.Briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Work to review", review.Briefing, StringComparison.Ordinal);
		string resolution = BriefingSections.Get(review.Briefing, "The resolution");
		Assert.Contains("Round 5, attempt 2, Resolve, Resolved by Merger.", resolution, StringComparison.Ordinal);
		Assert.Contains("Combined both versions", resolution, StringComparison.Ordinal);
		Assert.Contains("RESOLUTIONREPORT", resolution, StringComparison.Ordinal);
		Assert.Contains("SECONDGATEOUTPUT", BriefingSections.Get(review.Briefing, "Gate results"), StringComparison.Ordinal);

		PipelineWork mergeAgain = await rig.SucceedAsync(review);

		Assert.Equal(RoundKind.Merge, mergeAgain.Kind);
		Assert.Equal(2, mergeAgain.Attempt);
		Assert.Null(await rig.RecordAsync(mergeAgain, RoundResult.Succeeded));
		Assert.Equal(["Work@1:Completed", "Gate@1:Passed", "Review@1:Approved", "Merge@1:Conflicted", "Resolve@2:Resolved", "Gate@2:Passed", "Review@2:Approved", "Merge@2:Merged"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		WorkCard? complete = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(complete);
		Assert.Equal(CardLane.Complete, complete.Lane);
		Assert.Equal(2, complete.Attempt);
	}

	[Fact]
	public async Task RecordResultAsync_OfAResolutionForACardWithoutAReviewer_RecordsTheReviewAsSkippedAndGoesOnToTheMerge()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));

		PipelineWork gate = await rig.SucceedAsync(resolve);
		PipelineWork merge = await rig.SucceedAsync(gate);

		Assert.Equal(RoundKind.Merge, merge.Kind);
		Assert.Equal(2, merge.Attempt);
		Assert.Equal(["Work@1:Completed", "Gate@1:Passed", "Review@1:Skipped", "Merge@1:Conflicted", "Resolve@2:Resolved", "Gate@2:Passed", "Review@2:Skipped", "Merge@2:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task RecordResultAsync_OfAResolutionWithoutEnabledGates_RecordsTheGatesAsSkippedAndStillOpensTheReview()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));
		await PipelineArrangement.DisableGatesAsync(rig);

		PipelineWork review = await rig.SucceedAsync(resolve);

		Assert.Equal(RoundKind.Review, review.Kind);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Resolved", "Gate@2:Skipped", "Review@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(4));
	}

	[Fact]
	public async Task RecordResultAsync_OfARejectionOfTheResolution_SendsTheCardBackToItsWorkerWithTheFindings()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));
		PipelineWork review = await rig.AdvanceToAsync(resolve, RoundKind.Review);

		PipelineWork? work = await rig.RecordAsync(review, RoundResult.Failed, "The merge dropped the plus-sign check", "FINDINGS");

		Assert.NotNull(work);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(3, work.Attempt);
		Assert.Equal(TestData.DefaultAgentName, work.Agent?.Name);
		Assert.NotNull(work.Briefing);
		string reason = BriefingSections.Get(work.Briefing, "Why this card came back");
		Assert.Contains("FINDINGS", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", work.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Resolve@2:Resolved", "Gate@2:Passed", "Review@2:Rejected", "Work@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(4));
	}

	[Fact]
	public async Task RecordResultAsync_OfAGateFailureAfterAResolution_SendsTheCardBackToItsWorkerAndTheReviewAfterThatReviewsTheWorkAgain()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));
		PipelineWork gate = await rig.AdvanceToAsync(resolve, RoundKind.Gate);

		PipelineWork? work = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed after the merge", "GATEFAILURE");

		Assert.NotNull(work);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(3, work.Attempt);
		Assert.NotNull(work.Briefing);
		Assert.Contains("GATEFAILURE", BriefingSections.Get(work.Briefing, "Why this card came back"), StringComparison.Ordinal);
		Assert.DoesNotContain("git merge", work.Briefing, StringComparison.Ordinal);
		PipelineWork secondGate = await rig.SucceedAsync(work);
		PipelineWork review = await rig.SucceedAsync(secondGate);
		Assert.Equal(RoundKind.Review, review.Kind);
		Assert.NotNull(review.Briefing);
		Assert.Contains("## Work to review", review.Briefing, StringComparison.Ordinal);
		Assert.DoesNotContain("## Merge to review", review.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Resolve@2:Resolved", "Gate@2:Failed", "Work@3:Completed", "Gate@3:Passed", "Review@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(5));
	}

	[Fact]
	public async Task RecordResultAsync_OfAnOutdatedMergeAfterAnApprovedResolution_OnlyGatesAgainBecauseTheApprovalStands()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));
		PipelineWork merge = await rig.AdvanceToAsync(resolve, RoundKind.Merge);

		PipelineWork? gate = await rig.RecordAsync(merge, RoundResult.Outdated, "release moved on");
		Assert.NotNull(gate);
		PipelineWork next = await rig.SucceedAsync(gate);

		Assert.Equal(RoundKind.Gate, gate.Kind);
		Assert.Equal(RoundKind.Merge, next.Kind);
		Assert.Equal(["Review@2:Approved", "Merge@2:Outdated", "Gate@2:Passed", "Merge@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(4));
	}

	[Fact]
	public async Task RecordResultAsync_OfASecondConflictAfterAResolution_OpensAnotherResolveRoundOnTheNextAttemptQuotingOnlyTheLatestConflict()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork firstResolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge), "FIRSTCONFLICTNOTE", "FIRSTCONFLICTREPORT");
		PipelineWork secondMerge = await rig.AdvanceToAsync(firstResolve, RoundKind.Merge);

		PipelineWork secondResolve = await rig.ConflictAsync(secondMerge, "SECONDCONFLICTNOTE", "SECONDCONFLICTREPORT");

		Assert.Equal(RoundKind.Resolve, secondResolve.Kind);
		Assert.Equal(3, secondResolve.Attempt);
		Assert.NotNull(secondResolve.Briefing);
		string conflict = BriefingSections.Get(secondResolve.Briefing, "Why the merge stopped");
		Assert.Contains("SECONDCONFLICTREPORT", conflict, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTCONFLICTREPORT", conflict, StringComparison.Ordinal);
		Assert.Contains("- Stage: Resolve (attempt 3 of 5; round 9 of this card)", secondResolve.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Resolve@2:Resolved", "Gate@2:Passed", "Review@2:Approved", "Merge@2:Conflicted", "Resolve@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(6));
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictAfterTheMergeAgentWasAdded_OpensAResolveRoundWhileAnEarlierConflictWentToTheWorker()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork work = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));
		Assert.Equal(RoundKind.Work, work.Kind);
		await rig.AddMergerAsync();

		PipelineWork secondMerge = await rig.AdvanceToAsync(work, RoundKind.Merge);
		PipelineWork resolve = await rig.ConflictAsync(secondMerge);

		Assert.Equal(RoundKind.Resolve, resolve.Kind);
		Assert.Equal(3, resolve.Attempt);
		Assert.Equal(["Merge@1:Conflicted", "Work@2:Completed", "Gate@2:Passed", "Review@2:Approved", "Merge@2:Conflicted", "Resolve@3:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(6));
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictWhileTheMergeAgentIsBeingRemoved_FallsBackToTheWorkerBecauseTheSetupIsReadWithTheCard()
	{
		await using PipelineRig rig = await CreateRigAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		Agent merger = Assert.Single(await rig.Agents.ListAsync(TestContext.Current.CancellationToken), agent => agent.CanMerge);
		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(merger.Id, TestContext.Current.CancellationToken));

		PipelineWork next = await rig.ConflictAsync(merge, ConflictNote, ConflictReport);

		Assert.Equal(RoundKind.Work, next.Kind);
		Assert.Equal(TestData.DefaultAgentName, next.Agent?.Name);
		Assert.Equal(["Merge@1:Conflicted", "Work@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
	}
}
