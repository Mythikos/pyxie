using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Two callers can each pass the check that no agent resolves merge conflicts and then both try to make one. The project's database allows a single merge agent through its filtered unique index; the service reports the loser's refusal as the same typed result as a check that finds the other agent first, and checks again after its own write for a database without that index. Whoever wins, exactly one merge agent is left.</summary>
public sealed class AgentServiceMergeRaceTests
{
	private const string TakenMessage = "Agent 'Merger' already resolves merge conflicts. Only one agent can.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static AgentInput Input(string name, bool canMerge = true)
	{
		return new AgentInput(name, HarnessKind.ClaudeCode, "claude", string.Empty, CanWork: true, CanReview: true, canMerge);
	}

	private static async Task<List<string>> MergeAgentNamesAsync(PipelineRig rig)
	{
		return [.. (await rig.Agents.ListAsync(Token)).Where(agent => agent.CanMerge).Select(agent => agent.Name)];
	}

	[Fact]
	public async Task CreateAsync_WhenAnotherCallerCreatesTheMergeAgentAfterTheCheck_IsRefusedByTheIndexAndLeavesTheWinnersAgentAlone()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.CreateAsync(Input("Merger"), Token)));

		string message = ResultAssert.Failed(await rig.Agents.CreateAsync(Input("Late"), Token));

		interceptor.AssertFired();
		Assert.Equal(TakenMessage, message);
		Assert.Equal(["Merger"], await MergeAgentNamesAsync(rig));
		Assert.Single(await rig.Agents.ListAsync(Token));
	}

	[Fact]
	public async Task UpdateAsync_WhenAnotherCallerCreatesTheMergeAgentAfterTheCheck_IsRefusedByTheIndexAndChangesNothing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent other = await TestData.AddAgentAsync(rig.Database, "Other", canWork: true, canReview: true);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.CreateAsync(Input("Merger"), Token)));

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(other.Id, Input("Renamed"), Token));

		interceptor.AssertFired();
		Assert.Equal(TakenMessage, message);
		Agent? stored = await TestData.FindAgentAsync(rig.Database, other.Id);
		Assert.NotNull(stored);
		Assert.Equal("Other", stored.Name);
		Assert.False(stored.CanMerge, "The refused change must not be applied.");
		Assert.Equal(["Merger"], await MergeAgentNamesAsync(rig));
	}

	[Fact]
	public async Task UpdateAsync_WhenAnotherCallerGivesTheMergeRoleToADifferentAgentAfterTheCheck_IsRefusedAndTheRoleStaysWithThatAgent()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent first = await TestData.AddAgentAsync(rig.Database, "Merger", canWork: true, canReview: true);
		Agent second = await TestData.AddAgentAsync(rig.Database, "Second", canWork: true, canReview: true);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.UpdateAsync(first.Id, Input("Merger"), Token)));

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(second.Id, Input("Second"), Token));

		interceptor.AssertFired();
		Assert.Equal(TakenMessage, message);
		Assert.Equal(["Merger"], await MergeAgentNamesAsync(rig));
		Assert.False((await TestData.FindAgentAsync(rig.Database, second.Id))?.CanMerge, "The loser must not have the role.");
	}

	[Fact]
	public async Task CreateAsync_WhenAnotherCallerTakesTheNameAfterTheCheck_ReportsTheDuplicateNameNotAMergeRefusal()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.CreateAsync(Input("Alpha", canMerge: false), Token)));

		string message = ResultAssert.Failed(await rig.Agents.CreateAsync(Input("Alpha", canMerge: false), Token));

		interceptor.AssertFired();
		Assert.Equal("An agent named 'Alpha' already exists.", message);
		Assert.Empty(await MergeAgentNamesAsync(rig));
	}

	[Fact]
	public async Task CreateAsync_WithoutTheDatabaseIndex_StillRefusesAndRollsBackWhenAnotherCallerCreatesTheMergeAgentAfterTheCheck()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await DropMergeIndexAsync(rig);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.CreateAsync(Input("Merger"), Token)));

		string message = ResultAssert.Failed(await rig.Agents.CreateAsync(Input("Late"), Token));

		interceptor.AssertFired();
		Assert.Equal(TakenMessage, message);
		Assert.Equal(["Merger"], await MergeAgentNamesAsync(rig));
		Assert.Single(await rig.Agents.ListAsync(Token));
	}

	[Fact]
	public async Task UpdateAsync_WithoutTheDatabaseIndex_StillRefusesAndRollsBackWhenAnotherCallerCreatesTheMergeAgentAfterTheCheck()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		await DropMergeIndexAsync(rig);
		Agent other = await TestData.AddAgentAsync(rig.Database, "Other", canWork: true, canReview: true);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.CreateAsync(Input("Merger"), Token)));

		string message = ResultAssert.Failed(await rig.Agents.UpdateAsync(other.Id, Input("Renamed"), Token));

		interceptor.AssertFired();
		Assert.Equal(TakenMessage, message);
		Agent? stored = await TestData.FindAgentAsync(rig.Database, other.Id);
		Assert.NotNull(stored);
		Assert.Equal("Other", stored.Name);
		Assert.False(stored.CanMerge, "The write must be rolled back with the refusal.");
		Assert.Equal(["Merger"], await MergeAgentNamesAsync(rig));
	}

	[Fact]
	public async Task CreateAsync_WithoutTheDatabaseIndex_StillCreatesTheOnlyMergeAgent()
	{
		await using PipelineRig rig = new();
		await DropMergeIndexAsync(rig);

		ResultAssert.Succeeded(await rig.Agents.CreateAsync(Input("Merger"), Token));

		Assert.Equal(["Merger"], await MergeAgentNamesAsync(rig));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task CreateAsync_WhenTheSaveFailsForAReasonThatIsNeitherTheNameNorTheMergeRole_IsNotReportedAsARefusal(bool canMerge)
	{
		ThrowingSaveInterceptor interceptor = new();
		await using PipelineRig rig = new(interceptor);
		interceptor.Arm();

		await Assert.ThrowsAsync<DbUpdateException>(() => rig.Agents.CreateAsync(Input("Agent", canMerge), Token));

		Assert.Empty(await rig.Agents.ListAsync(Token));
	}

	[Fact]
	public async Task UpdateAsync_WhenTheSaveFailsForAReasonThatIsNeitherTheNameNorTheMergeRole_IsNotReportedAsARefusal()
	{
		ThrowingSaveInterceptor interceptor = new();
		await using PipelineRig rig = new(interceptor);
		Agent agent = await TestData.AddAgentAsync(rig.Database, "Agent", canWork: true, canReview: true);
		interceptor.Arm();

		await Assert.ThrowsAsync<DbUpdateException>(() => rig.Agents.UpdateAsync(agent.Id, Input("Agent"), Token));

		Assert.False((await TestData.FindAgentAsync(rig.Database, agent.Id))?.CanMerge, "The failed write must not be applied.");
	}

	private static async Task DropMergeIndexAsync(PipelineRig rig)
	{
		await using ProjectDbContext context = rig.Database.CreateContext();
		await context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Agents_CanMerge\"", Token);
	}
}
