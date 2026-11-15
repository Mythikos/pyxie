using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Without an open project or a connected repository the executor refuses without starting anything, and a git failure, such as a missing branch, stops the card for a person instead of being retried, because another attempt would fail the same way.</summary>
public sealed class RoundExecutorRepositoryTests
{
	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task RunAsync_WithNoProjectOpen_BlocksTheRoundWithoutStartingAnything(RoundKind kind)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync(connectRepository: false);
		string record = Path.Combine(rig.MarkerFolder, "agent.jsonl");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record);

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(kind, agent);

		Assert.Equal((RoundResult.Blocked, "No project is open."), (verdict.Result, verdict.Note));
		Assert.Null(repository);
		Assert.False(File.Exists(record), "No agent may start when there is no project to work in.");
		Assert.Empty(rig.StartedSandboxes());
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task RunAsync_WithAProjectButNoRepository_BlocksTheRoundWithoutStartingAnything(RoundKind kind)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "agent.jsonl");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record);
		await rig.Runtime.RemoveRepositoryRowAsync();

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(kind, agent);

		Assert.Equal((RoundResult.Blocked, "No repository is connected."), (verdict.Result, verdict.Note));
		Assert.Null(repository);
		Assert.False(File.Exists(record), "No agent may start when there is no repository to work in.");
		Assert.Empty(rig.StartedSandboxes());
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	public async Task RunAsync_ForAGateOrReviewRoundWithoutTheCardsBranch_IsBlockedAskingForTheCardToBeEditedWithoutStartingASandbox(RoundKind kind)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "reviewer.jsonl");
		AgentSnapshot reviewer = FakeHarnessAgent.Snapshot("Reviewer", HarnessKind.Other, "--record", record, "--outcome", "approved");

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(kind, kind == RoundKind.Review ? reviewer : null);

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.StartsWith($"git failed: The card's branch {RoundExecutorRig.Branch} is not in the repository", verdict.Note, StringComparison.Ordinal);
		Assert.EndsWith("Edit the card so it starts again at work.", verdict.Note, StringComparison.Ordinal);
		Assert.Equal(rig.Runtime.Repository?.Id, repository?.Id);
		Assert.False(File.Exists(record), "The reviewer must not start when there is no work to review.");
		Assert.Empty(rig.StartedSandboxes());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenTheTargetBranchIsGone_BlocksTheFirstWorkRoundWithGitsComplaintAndNeverStartsTheAgent()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "worker.jsonl");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--record", record);
		await DeleteTargetBranchAsync(rig);

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(RoundKind.Work, worker);

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.StartsWith("git failed: ", verdict.Note, StringComparison.Ordinal);
		Assert.False(string.IsNullOrWhiteSpace(verdict.Report), "The report keeps everything git said.");
		Assert.Equal(rig.Runtime.Repository?.Id, repository?.Id);
		Assert.False(File.Exists(record), "The worker must not start when its sandbox could not be prepared.");
		Assert.Empty(rig.StartedSandboxes());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenTheTargetBranchIsGoneBeforeTheMerge_BlocksTheMergeRoundAndLeavesTheCardBranchAlone()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string cardTip = await rig.WorkAsync();
		await DeleteTargetBranchAsync(rig);

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Merge);

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.StartsWith("git failed: ", verdict.Note, StringComparison.Ordinal);
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
	}

	// Pointing HEAD at another branch first, because git will not delete the branch HEAD names.
	private static async Task DeleteTargetBranchAsync(RoundExecutorRig rig)
	{
		await rig.Hub.RunGitAsync("symbolic-ref", "HEAD", "refs/heads/elsewhere");
		await rig.Hub.RunGitAsync("branch", "--delete", "--force", TempGitRepository.MainBranch);
	}
}
