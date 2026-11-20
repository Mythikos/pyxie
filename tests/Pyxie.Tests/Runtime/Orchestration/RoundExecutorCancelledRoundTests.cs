using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Stopping a round part-way throws and takes nothing back: the card's branch stays at the worker's last commit, a gate or review sandbox goes, and a worker's sandbox stays with what the worker left, for the round to resume in. The agent or gate notes that it started and then sleeps far longer than the test, so the test cancels while it runs.</summary>
public sealed class RoundExecutorCancelledRoundTests
{
	private const string HoldMilliseconds = "60000";

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Gate, SandboxRole.Gates)]
	[InlineData(RoundKind.Review, SandboxRole.Review)]
	public async Task RunAsync_WhenAGateOrReviewRoundIsCancelled_ThrowsRemovesItsSandboxAndLeavesTheCardBranchAtTheWorkersCommit(RoundKind kind, SandboxRole role)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string started = Path.Combine(rig.MarkerFolder, "started.jsonl");
		string[] holding = ["--record", started, "--sleep", HoldMilliseconds];
		await rig.Runtime.AddGateAsync(new GateInput("Held gate", FakeHarnessAgent.Command, FakeHarnessAgent.Arguments(holding), TimeoutMinutes: 5, IsEnabled: true));
		AgentSnapshot reviewer = FakeHarnessAgent.Snapshot("Held reviewer", HarnessKind.Other, [.. holding, "--outcome", "approved"]);
		string workerCommit = await rig.WorkAsync();
		PipelineWork round = rig.NextRound(kind, kind == RoundKind.Review ? reviewer : null);
		using CancellationTokenSource cancellation = new();

		Task<(RoundVerdict Verdict, Repository? Repository)> run = rig.Runtime.RunRoundAsync(round, cancellation.Token);
		await rig.Runtime.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), $"the {kind} round to start");
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
		Assert.Equal(workerCommit, await rig.GetBranchTipAsync());
		Assert.False(Directory.Exists(rig.Runtime.SandboxWorkFolder(RoundExecutorRig.CardId, role)), $"The {kind} sandbox goes when its round is stopped.");
		Assert.Empty(await rig.GetWorkerChangesAsync());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenAWorkRoundIsCancelled_ThrowsTakesNothingBackAndKeepsTheWorkersSandbox()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string started = Path.Combine(rig.MarkerFolder, "started.jsonl");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Held worker", HarnessKind.Other, "--record", started, "--sleep", HoldMilliseconds, "--write", "feature.txt=too late");
		PipelineWork round = rig.NextRound(RoundKind.Work, worker);
		using CancellationTokenSource cancellation = new();

		Task<(RoundVerdict Verdict, Repository? Repository)> run = rig.Runtime.RunRoundAsync(round, cancellation.Token);
		await rig.Runtime.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), "the work round to start");
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
		Assert.False(await rig.BranchExistsAsync(), "A stopped work round takes nothing back.");
		Assert.True(File.Exists(Path.Combine(rig.WorkerRepository, PipelineContract.BriefingFile)), "The worker's sandbox stays, with the round's briefing, for the round to resume in.");
		Assert.False(File.Exists(Path.Combine(rig.WorkerRepository, "feature.txt")), "The worker was stopped before it wrote its file.");
	}
}
