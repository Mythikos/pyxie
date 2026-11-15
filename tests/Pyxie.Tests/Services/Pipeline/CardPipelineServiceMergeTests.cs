using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>The merge round runs in the Review lane after the review: merging completes the card, a conflict is a send-back that tells the worker why when the project has no merge agent (see <see cref="CardPipelineServiceResolveTests"/> for when it has one), and a work round can never be reported as failed.</summary>
public sealed class CardPipelineServiceMergeTests
{
	[Fact]
	public async Task RecordResultAsync_OfAnApprovedReview_OpensTheMergeInTheReviewLaneWithoutMovingTheCardOrGivingItAnAgent()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await TestData.AddCardAsync(rig.Database, "Other", CardLane.Review);
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork review = await rig.ClaimAtAsync(RoundKind.Review);
		await TestData.AddCardAsync(rig.Database, "Behind", CardLane.Review);
		Assert.Equal("Other,Card,Behind", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));

		PipelineWork? merge = await rig.RecordAsync(review, RoundResult.Succeeded, report: "Looks good");

		Assert.NotNull(merge);
		Assert.Equal(card.Id, merge.CardId);
		Assert.Equal("Card", merge.CardName);
		Assert.Equal(RoundKind.Merge, merge.Kind);
		Assert.Equal(4, merge.Sequence);
		Assert.Equal(1, merge.Attempt);
		Assert.Null(merge.Agent);
		Assert.Null(merge.Briefing);
		Assert.Equal("Other,Card,Behind", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Review));
		Assert.Equal(["Work@1:Completed", "Gate@1:Passed", "Review@1:Approved", "Merge@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task RecordResultAsync_OfAMerge_CompletesTheCardAtTheBottomOfCompleteAndClosesTheGapInReview()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await TestData.AddLaneAsync(rig.Database, CardLane.Complete, "Done 1", "Done 2");
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await PipelineArrangement.ClaimAtAttemptAsync(rig, RoundKind.Merge, attempt: 2);
		await TestData.AddCardAsync(rig.Database, "Behind", CardLane.Review);
		DateTime mergedAt = rig.Database.Clock.Tick();

		RoundReport report = new("Merged into main", "Merge commit 1a2b3c4");
		ServiceResult<PipelineWork?> result = await rig.Pipeline.RecordResultAsync(merge.RoundId, RoundResult.Succeeded, report, TestContext.Current.CancellationToken);

		Assert.True(result.Succeeded, $"The merge should be accepted, but got: {result.Error}");
		Assert.Null(result.Value);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Complete, stored.Lane);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(2, stored.Attempt);
		Assert.Null(stored.ResumeRound);
		Assert.Equal(mergedAt, stored.UpdatedUtc);
		Assert.Equal("Done 1,Done 2,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Complete));
		Assert.Equal("0,1,2", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Complete));
		Assert.Equal("Behind", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.Review));
		Assert.Equal("0", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.Review));
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		CardRound merged = rounds[^1];
		Assert.Equal(merge.RoundId, merged.Id);
		Assert.Equal(RoundOutcome.Merged, merged.Outcome);
		Assert.Equal(2, merged.Attempt);
		Assert.Equal("Merged into main", merged.Note);
		Assert.Equal("Merge commit 1a2b3c4", merged.Report);
		Assert.Equal(mergedAt, merged.EndedUtc);
		Assert.DoesNotContain(rounds, round => round.Outcome is null);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task RecordResultAsync_OfAConflictedMergeWithoutAMergeAgent_SendsTheCardBackToWorkAndQuotesTheConflictAndTheMergeInstructionToTheWorker()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await TestData.AddCardAsync(rig.Database, "Busy", CardLane.InProgress);

		string conflict = "CONFLICT (content): Merge conflict in src/Login.cs\nCONFLICT (content): Merge conflict in src/Auth.cs";
		PipelineWork? work = await rig.RecordAsync(merge, RoundResult.Failed, "Conflicts in src/Login.cs and src/Auth.cs.", conflict);

		Assert.NotNull(work);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(5, work.Sequence);
		Assert.Equal(2, work.Attempt);
		Assert.Equal(TestData.DefaultAgentName, work.Agent?.Name);
		Assert.NotNull(work.Briefing);
		string reason = BriefingSections.Get(work.Briefing, "Why this card came back");
		Assert.Contains("Round 4, attempt 1, Merge, Conflicted.", reason, StringComparison.Ordinal);
		Assert.Contains("Conflicts in src/Login.cs and src/Auth.cs.", reason, StringComparison.Ordinal);
		Assert.Contains("CONFLICT (content): Merge conflict in src/Login.cs\nCONFLICT (content): Merge conflict in src/Auth.cs", reason, StringComparison.Ordinal);
		Assert.Contains("Merge `main` into this branch with `git merge main`", reason, StringComparison.Ordinal);
		Assert.Contains("- Stage: Work (attempt 2 of 5; round 5 of this card)", work.Briefing, StringComparison.Ordinal);
		Assert.Equal(["Merge@1:Conflicted", "Work@2:Open"], (await PipelineRoundLog.ReadAsync(rig.Database, card.Id)).TakeLast(2));
		Assert.Equal("Busy,Card", await TestData.ReadLaneNamesAsync(rig.Database, CardLane.InProgress));
		Assert.Equal("0,1", await TestData.ReadLaneSortOrdersAsync(rig.Database, CardLane.InProgress));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(2, stored.Attempt);
		Assert.Equal(CardState.Ready, stored.State);
	}

	[Fact]
	public async Task RecordResultAsync_OfASecondConflict_QuotesOnlyTheLatestConflictToTheWorker()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork firstMerge = await rig.ClaimAtAsync(RoundKind.Merge);
		PipelineWork? work = await rig.RecordAsync(firstMerge, RoundResult.Failed, "FIRSTCONFLICTNOTE", "FIRSTCONFLICTREPORT");
		Assert.NotNull(work);
		PipelineWork secondMerge = await rig.AdvanceToAsync(work, RoundKind.Merge);

		PipelineWork? again = await rig.RecordAsync(secondMerge, RoundResult.Failed, "SECONDCONFLICTNOTE", "SECONDCONFLICTREPORT");

		Assert.NotNull(again);
		Assert.NotNull(again.Briefing);
		string reason = BriefingSections.Get(again.Briefing, "Why this card came back");
		Assert.Contains("SECONDCONFLICTNOTE", reason, StringComparison.Ordinal);
		Assert.Contains("SECONDCONFLICTREPORT", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTCONFLICTREPORT", reason, StringComparison.Ordinal);
		Assert.DoesNotContain("FIRSTCONFLICTNOTE", reason, StringComparison.Ordinal);
		Assert.Equal(3, again.Attempt);
	}

	[Fact]
	public async Task RecordResultAsync_OfAWorkRoundReportingFailure_IsRefusedWithTheReasonAndTheRoundStaysOpen()
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card");
		PipelineWork work = await rig.ClaimAsync();
		WorkCard? before = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(before);

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Failed, new RoundReport("Could not finish", null), TestContext.Current.CancellationToken));

		Assert.Equal("A work round cannot fail. Report Errored or Blocked with the reason instead.", message);
		await CardAssert.UnchangedAsync(rig.Database, before, expectedCardCount: 1);
		Assert.Equal(["Work@1:Open"], await PipelineRoundLog.ReadAsync(rig.Database, card.Id));
		PipelineWork? gate = await rig.RecordAsync(work, RoundResult.Succeeded);
		Assert.NotNull(gate);
		Assert.Equal(RoundKind.Gate, gate.Kind);
	}
}
