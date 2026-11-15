using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>The rig's contract, which pipeline tests build on: what it starts with, and what it takes to make a card reach each stage.</summary>
public sealed class PipelineRigTests
{
	[Fact]
	public async Task NewRig_HasAConnectedRepositoryOnMainAndNoAgentsCardsOrGates()
	{
		await using PipelineRig rig = new();

		Assert.Equal("main", rig.Repository.TargetBranch);
		Assert.Equal(TestData.RepositoryPath, rig.Repository.Path);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
		Assert.Equal(0, await TestData.CountCardsAsync(rig.Database));
		Assert.Empty(await rig.Agents.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task NewRig_ClaimsACardWhenOneWaitsAndNoRepositoryIsLeftToConnect()
	{
		await using PipelineRig rig = new();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");

		PipelineWork work = await rig.ClaimAsync();

		Assert.Equal(card.Id, work.CardId);
	}

	[Fact]
	public async Task RemoveRepositoryAsync_LeavesNothingToClaimInto()
	{
		await using PipelineRig rig = new();
		await TestData.AddCardAsync(rig.Database, "Card");

		await rig.RemoveRepositoryAsync();

		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task AddGateAsync_MakesCardsGetAGateRound_WhileADisabledGateDoesNot()
	{
		await using PipelineRig disabled = new();
		await disabled.AddGateAsync(isEnabled: false);
		await TestData.AddCardAsync(disabled.Database, "Card");
		await using PipelineRig enabled = new();
		await enabled.AddGateAsync();
		await TestData.AddCardAsync(enabled.Database, "Card");

		PipelineWork afterDisabledGate = await disabled.SucceedAsync(await disabled.ClaimAsync());
		PipelineWork afterEnabledGate = await enabled.SucceedAsync(await enabled.ClaimAsync());

		Assert.Equal(RoundKind.Merge, afterDisabledGate.Kind);
		Assert.Equal(RoundKind.Gate, afterEnabledGate.Kind);
	}

	[Fact]
	public async Task ClaimAtAsync_ReachesEveryKindOfRoundForACardWithAGateAndAReviewer()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);

		PipelineWork work = await rig.ClaimAtAsync(RoundKind.Work);
		PipelineWork gate = await rig.AdvanceToAsync(work, RoundKind.Gate);
		PipelineWork review = await rig.AdvanceToAsync(gate, RoundKind.Review);
		PipelineWork merge = await rig.AdvanceToAsync(review, RoundKind.Merge);

		Assert.Equal([RoundKind.Work, RoundKind.Gate, RoundKind.Review, RoundKind.Merge], new[] { work.Kind, gate.Kind, review.Kind, merge.Kind });
	}

	[Fact]
	public async Task ClaimAtAsync_ForAGateRoundWithoutAnEnabledGate_SaysWhyItWasNotReached()
	{
		await using PipelineRig rig = new();
		await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);

		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.ClaimAtAsync(RoundKind.Gate));

		Assert.Contains("No Gate round opened", error.Message, StringComparison.Ordinal);
		Assert.Contains("AddGateAsync", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ClaimAtAsync_ForAReviewRoundWithoutAReviewer_SaysWhyItWasNotReached()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		await TestData.AddCardAsync(rig.Database, "Card");

		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.ClaimAtAsync(RoundKind.Review));

		Assert.Contains("No Review round opened", error.Message, StringComparison.Ordinal);
		Assert.Contains("reviewer", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AddMergerAsync_AddsTheProjectsOnlyMergeAgentWhichDiffersFromTheDefaultAgentAndHasNoOtherRole()
	{
		await using PipelineRig rig = new();
		Agent defaultAgent = await TestData.EnsureDefaultAgentAsync(rig.Database);

		Agent merger = await rig.AddMergerAsync();

		Assert.Equal(PipelineRig.MergerName, merger.Name);
		Assert.True(merger.CanMerge);
		Assert.False(merger.CanWork);
		Assert.False(merger.CanReview);
		Assert.NotEqual(defaultAgent.Harness, merger.Harness);
		Assert.NotEqual(defaultAgent.Command, merger.Command);
		Assert.NotEqual(defaultAgent.Arguments, merger.Arguments);
		Assert.Equal([PipelineRig.MergerName], (await rig.Agents.ListAsync(TestContext.Current.CancellationToken)).Where(agent => agent.CanMerge).Select(agent => agent.Name));
	}

	[Fact]
	public async Task ConflictAsync_OpensAResolveRoundWhenTheProjectHasAMergeAgent_AndAWorkRoundWhenItHasNone()
	{
		await using PipelineRig withMerger = new();
		await withMerger.AddMergerAsync();
		await TestData.AddCardAsync(withMerger.Database, "Card");
		await using PipelineRig without = new();
		await TestData.AddCardAsync(without.Database, "Card");

		PipelineWork resolve = await withMerger.ConflictAsync(await withMerger.ClaimAtAsync(RoundKind.Merge));
		PipelineWork work = await without.ConflictAsync(await without.ClaimAtAsync(RoundKind.Merge), "Conflicts in src/Other.cs.", "CONFLICT");

		Assert.Equal(RoundKind.Resolve, resolve.Kind);
		Assert.Equal(RoundKind.Work, work.Kind);
		Assert.Equal(2, resolve.Attempt);
		Assert.Equal(2, work.Attempt);
	}

	[Fact]
	public async Task ConflictAsync_OfARoundThatIsNotAMerge_Fails()
	{
		await using PipelineRig rig = new();
		await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork work = await rig.ClaimAsync();

		await Assert.ThrowsAsync<Xunit.Sdk.EqualException>(() => rig.ConflictAsync(work));
	}

	[Fact]
	public async Task AdvanceToAsync_FromAResolveRound_ReachesTheGatesTheReviewAndTheMergeInThatOrder()
	{
		await using PipelineRig rig = new();
		await rig.AddMergerAsync();
		await rig.AddGateAsync();
		await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));

		PipelineWork same = await rig.AdvanceToAsync(resolve, RoundKind.Resolve);
		PipelineWork gate = await rig.AdvanceToAsync(resolve, RoundKind.Gate);
		PipelineWork review = await rig.AdvanceToAsync(gate, RoundKind.Review);
		PipelineWork merge = await rig.AdvanceToAsync(review, RoundKind.Merge);

		Assert.Equal(RoundKind.Resolve, resolve.Kind);
		Assert.Equal(resolve.RoundId, same.RoundId);
		Assert.Equal([RoundKind.Gate, RoundKind.Review, RoundKind.Merge], new[] { gate.Kind, review.Kind, merge.Kind });
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task AdvanceToAsync_ToAResolveRoundFromAnotherKind_SaysOnlyAConflictOpensOne(RoundKind from)
	{
		await using PipelineRig rig = new();
		await rig.AddMergerAsync();
		await rig.AddGateAsync();
		await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(from);

		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.AdvanceToAsync(running, RoundKind.Resolve));

		Assert.Contains("ConflictAsync", error.Message, StringComparison.Ordinal);
		Assert.Equal(from, running.Kind);
	}

	[Fact]
	public async Task AdvanceToAsync_FromAResolveRoundToAReviewTheCardDoesNotGet_SaysWhyItWasNotReached()
	{
		await using PipelineRig rig = new();
		await rig.AddMergerAsync();
		await TestData.AddCardAsync(rig.Database, "Card");
		PipelineWork resolve = await rig.ConflictAsync(await rig.ClaimAtAsync(RoundKind.Merge));

		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.AdvanceToAsync(resolve, RoundKind.Review));

		Assert.Contains("No Review round opened", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CompleteAsync_RunsACardToTheMergeThatCompletesIt()
	{
		await using PipelineRig rig = new();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");

		await rig.CompleteAsync(await rig.ClaimAsync());

		Assert.Equal(CardLane.Complete, (await TestData.FindCardAsync(rig.Database, card.Id))?.Lane);
	}

	[Fact]
	public async Task CreateCardAsync_GivesTheDefaultAgentAsWorkerAndNoReviewerUnlessAskedAndTheGivenAttemptLimit()
	{
		await using PipelineRig rig = new();

		WorkCard plain = await rig.CreateCardAsync("Plain");
		WorkCard reviewed = await rig.CreateCardAsync("Reviewed", reviewerAgentId: TestData.DefaultAgent, attemptLimit: 7);

		Agent defaultAgent = await TestData.EnsureDefaultAgentAsync(rig.Database);
		Assert.Equal(defaultAgent.Id, plain.WorkerAgentId);
		Assert.Null(plain.ReviewerAgentId);
		Assert.Equal(5, plain.AttemptLimit);
		Assert.Equal(defaultAgent.Id, reviewed.WorkerAgentId);
		Assert.Equal(defaultAgent.Id, reviewed.ReviewerAgentId);
		Assert.Equal(7, reviewed.AttemptLimit);
	}
}
