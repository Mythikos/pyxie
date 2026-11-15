using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A round keeps a copy of the agent as it was when the round opened, so history survives the agent being renamed, reconfigured, or deleted.</summary>
public sealed class CardPipelineServiceSnapshotTests
{
	[Fact]
	public async Task OpenedRounds_KeepTheAgentAsItWasWhenTheyOpenedAfterTheAgentIsRenamedAndReconfigured()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false);
		WorkCard card = await rig.CreateCardAsync("Card", agent.Id);
		PipelineWork work = await rig.ClaimAsync();

		ResultAssert.Succeeded(await rig.Agents.UpdateAsync(agent.Id, new AgentInput("Wendy", HarnessKind.Codex, "codex", "--full-auto", true, false), TestContext.Current.CancellationToken));
		PipelineWork gate = await rig.SucceedAsync(work);
		PipelineWork? again = await rig.RecordAsync(gate, RoundResult.Failed, "Build failed");

		Assert.NotNull(again);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		AgentSnapshot? original = rounds[0].Agent;
		AgentSnapshot? reopened = rounds[2].Agent;
		Assert.NotNull(original);
		Assert.Equal("Wanda", original.Name);
		Assert.Equal(HarnessKind.ClaudeCode, original.Harness);
		Assert.Equal("claude", original.Command);
		Assert.Equal(string.Empty, original.Arguments);
		Assert.NotNull(reopened);
		Assert.Equal("Wendy", reopened.Name);
		Assert.Equal(HarnessKind.Codex, reopened.Harness);
		Assert.Equal("codex", reopened.Command);
		Assert.Equal("--full-auto", reopened.Arguments);
		Assert.Equal("Wendy", again.Agent?.Name);
	}

	[Fact]
	public async Task History_KeepsTheAgentAfterTheCardIsCompleteAndTheAgentIsDeleted()
	{
		await using PipelineRig rig = new();
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: false);
		WorkCard card = await rig.CreateCardAsync("Card", agent.Id);
		PipelineWork work = await rig.ClaimAsync();
		ResultAssert.Failed(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));
		await rig.CompleteAsync(work);

		ResultAssert.Succeeded(await rig.Agents.DeleteAsync(agent.Id, TestContext.Current.CancellationToken));

		Assert.Null(await TestData.FindAgentAsync(rig.Database, agent.Id));
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Null(stored.WorkerAgentId);
		IReadOnlyList<CardRound> history = ResultAssert.Succeeded(await rig.Cards.GetHistoryAsync(card.Id, TestContext.Current.CancellationToken));
		Assert.Equal("Wanda", history[0].Agent?.Name);
		Assert.Equal("claude", history[0].Agent?.Command);
		BoardCard onBoard = Assert.Single(await rig.Cards.GetBoardAsync(TestContext.Current.CancellationToken));
		Assert.Equal("Wanda", onBoard.Rounds[0].AgentName);
	}

	[Fact]
	public async Task OpenedRounds_OfAGateOrAMergeNeverHaveAnAgentEvenWhenTheCardHasBoth()
	{
		await using PipelineRig rig = new();
		await rig.AddGateAsync();
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Wanda", canWork: true, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card", worker.Id, worker.Id);

		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		PipelineWork merge = await rig.AdvanceToAsync(gate, RoundKind.Merge);

		Assert.Null(gate.Agent);
		Assert.Null(gate.Briefing);
		Assert.Null(merge.Agent);
		Assert.Null(merge.Briefing);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal([RoundKind.Work, RoundKind.Gate, RoundKind.Review, RoundKind.Merge], rounds.Select(round => round.Kind));
		Assert.Equal(["Wanda", null, "Wanda", null], rounds.Select(round => round.Agent?.Name));
		Assert.Equal([true, false, true, false], rounds.Select(round => round.Briefing is not null));
	}
}
