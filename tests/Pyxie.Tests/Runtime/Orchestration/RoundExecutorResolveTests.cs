using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A resolve round runs the merge agent in a fresh sandbox, in the middle of the merge Pyxie started. What the agent leaves is committed as the merge and taken back into the hub only when it is exactly one merge commit of the card's branch and the target branch, with no conflict markers left; otherwise the card's branch stays where it was and the round is an error the next attempt starts over from. A resolution that is taken back is reported with how the merge resolved each conflict, which the reviewer checks.</summary>
public sealed class RoundExecutorResolveTests
{
	private const string HoldMilliseconds = "60000";

	private static AgentSnapshot Merger(params string[] fakeArguments)
	{
		return FakeHarnessAgent.Snapshot("Merger", HarnessKind.Other, fakeArguments);
	}

	private static string Normalized(RoundVerdict verdict)
	{
		return verdict.Report?.ReplaceLineEndings("\n") ?? string.Empty;
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRound_RunsTheMergeAgentInTheMergeWaitingInAFreshSandboxAndTakesItsMergeCommitBack()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		(string cardTip, string targetTip) = await rig.ConflictAsync();
		AgentSnapshot merger = Merger("--git", "status --short", "--write", "README.md=resolved with both", "--outcome", "resolved", "--note", "kept both lines", "--report", "Kept the card's line and the target's line.");

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(RoundKind.Resolve, merger);

		string merge = await rig.GetBranchTipAsync();
		Assert.Equal((RoundResult.Succeeded, "kept both lines"), (verdict.Result, verdict.Note));
		Assert.Equal(rig.Runtime.Repository?.Id, repository?.Id);
		Assert.Equal($"{cardTip} {targetTip}", await rig.Hub.RunGitAsync("log", "-1", "--format=%P", RoundExecutorRig.Branch));
		Assert.Equal("Merge main into pyxie/card-1", await rig.Hub.RunGitAsync("log", "-1", "--format=%s", RoundExecutorRig.Branch));
		Assert.Equal("resolved with both", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "README.md"));
		Assert.Equal(targetTip, await rig.Hub.GetHashAsync("main"));
		Assert.Contains("UU README.md", await rig.ReadRoundLogAsync(sequence: 2), StringComparison.Ordinal);
		SandboxSpec sandbox = rig.StartedSandboxes()[^1];
		Assert.Equal((SandboxRole.Resolve, RoundExecutorRig.CardId), (sandbox.Role, sandbox.CardId));
		Assert.Empty(sandbox.Volumes);
		Assert.False(sandbox.Keep);
		Assert.False(Directory.Exists(rig.Runtime.SandboxWorkFolder(RoundExecutorRig.CardId, SandboxRole.Resolve)), "The resolve sandbox goes when its round ends.");
		Assert.Equal(string.Empty, await rig.Hub.RunGitAsync("for-each-ref", "refs/pyxie"));

		string report = Normalized(verdict);
		Assert.StartsWith("Kept the card's line and the target's line.\n\n## Resolution\n\nMerge commit " + merge + ", as `git show --remerge-diff` shows it:\n\n```diff\n", report, StringComparison.Ordinal);
		Assert.Contains("-<<<<<<<", report, StringComparison.Ordinal);
		Assert.Contains("+resolved with both", report, StringComparison.Ordinal);
		Assert.EndsWith("\n```", report, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhoseAgentSaysNothing_RecordsTheDefaultNoteAndOnlyTheResolutionInTheReport()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.ConflictAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--write", "README.md=resolved with both", "--outcome", "resolved"));

		Assert.Equal((RoundResult.Succeeded, "Resolved the conflicts of merging main."), (verdict.Result, verdict.Note));
		Assert.StartsWith("## Resolution\n\nMerge commit ", Normalized(verdict), StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhoseDiffHasBackticks_FencesItWithMoreOfThemSoItCannotEndTheBlock()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.ConflictAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--write", "README.md=```text\nresolved\n```", "--outcome", "resolved"));

		string report = Normalized(verdict);
		Assert.Contains("\n````diff\n", report, StringComparison.Ordinal);
		Assert.EndsWith("\n````", report, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhoseMergeWasClean_TakesTheMergeBackAndSaysThereWasNothingToResolve()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string cardTip = await rig.WorkAsync();
		string targetTip = await rig.MoveTargetAsync("moved-on.txt", "the target's file");

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--outcome", "resolved", "--note", "nothing to do"));

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal($"{cardTip} {targetTip}", await rig.Hub.RunGitAsync("log", "-1", "--format=%P", RoundExecutorRig.Branch));
		Assert.Equal("the target's file", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "moved-on.txt"));
		Assert.Equal($"## Resolution\n\nMerge commit {await rig.GetBranchTipAsync()}. `git show --remerge-diff` shows nothing: the merge had no conflicts to resolve.", Normalized(verdict));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhoseAgentLeftTheConflictMarkers_IsErroredNamingTheFileAndTakesNothingBack()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		(string cardTip, _) = await rig.ConflictAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--outcome", "resolved"));

		Assert.Equal((RoundResult.Errored, "Conflict markers are left in: README.md."), (verdict.Result, verdict.Note));
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
		Assert.Equal(string.Empty, await rig.Hub.RunGitAsync("for-each-ref", "refs/pyxie"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhoseAgentAbortedTheMerge_IsErroredBecauseTheMergeWasNotCompleted()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		(string cardTip, _) = await rig.ConflictAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--git", "merge --abort", "--outcome", "resolved"));

		Assert.Equal((RoundResult.Errored, "The merge of main was not completed."), (verdict.Result, verdict.Note));
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhoseAgentMadeAnExtraCommit_IsErroredBecauseItIsNotOneMergeCommit()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		(string cardTip, _) = await rig.ConflictAsync();
		AgentSnapshot merger = Merger("--write", "README.md=resolved with both", "--git", "add --all", "--git", "commit --no-edit", "--git", "commit --allow-empty --message extra", "--outcome", "resolved");

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, merger);

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.StartsWith("The resolution must be one merge commit of pyxie/card-1 and main. What came back is ", verdict.Note, StringComparison.Ordinal);
		Assert.Contains(", a commit with one parent, ", verdict.Note, StringComparison.Ordinal);
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData("blocked", RoundResult.Blocked)]
	[InlineData("errored", RoundResult.Errored)]
	public async Task RunAsync_ForAResolveRoundWhoseAgentDoesNotResolve_ReturnsItsVerdictAndTakesNothingBack(string outcome, RoundResult expected)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		(string cardTip, _) = await rig.ConflictAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--write", "README.md=half done", "--outcome", outcome, "--note", "the two changes cannot both hold"));

		Assert.Equal((expected, "the two changes cannot both hold"), (verdict.Result, verdict.Note));
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
		Assert.False(Directory.Exists(rig.Runtime.SandboxWorkFolder(RoundExecutorRig.CardId, SandboxRole.Resolve)));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundThatFollowsOneThatWasCutOffAfterTheBranchMoved_SucceedsWithoutRunningTheAgentAgain()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.ConflictAsync();
		(RoundVerdict first, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--write", "README.md=resolved with both", "--outcome", "resolved"));
		string merge = await rig.GetBranchTipAsync();
		int sandboxes = rig.StartedSandboxes().Count;
		string record = Path.Combine(rig.MarkerFolder, "second-agent.jsonl");

		(RoundVerdict second, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--record", record, "--outcome", "resolved"));

		Assert.Equal(RoundResult.Succeeded, first.Result);
		Assert.Equal((RoundResult.Succeeded, "main was already merged into pyxie/card-1."), (second.Result, second.Note));
		Assert.Equal(merge, await rig.GetBranchTipAsync());
		Assert.Equal(sandboxes, rig.StartedSandboxes().Count);
		Assert.False(File.Exists(record), "The agent must not run when there is nothing left to resolve.");
		Assert.Contains($"Merge commit {merge}, as `git show --remerge-diff` shows it:", Normalized(second), StringComparison.Ordinal);
		Assert.Contains("+resolved with both", Normalized(second), StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAResolveRoundWhenTheCardHasNoBranch_IsBlockedWithThePlainReason()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Resolve, Merger("--outcome", "resolved"));

		Assert.Equal(RoundResult.Blocked, verdict.Result);
		Assert.StartsWith("git failed: The card's branch pyxie/card-1 is not in the repository, so there is no work for this Resolve round.", verdict.Note, StringComparison.Ordinal);
		Assert.Empty(rig.StartedSandboxes());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenAResolveRoundIsCancelled_ThrowsRemovesItsSandboxAndLeavesTheCardBranchWhereItWas()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		(string cardTip, _) = await rig.ConflictAsync();
		string started = Path.Combine(rig.MarkerFolder, "started.jsonl");
		PipelineWork round = rig.NextRound(RoundKind.Resolve, Merger("--record", started, "--sleep", HoldMilliseconds, "--write", "README.md=too late", "--outcome", "resolved"));
		using CancellationTokenSource cancellation = new();

		Task<(RoundVerdict Verdict, Repository? Repository)> run = rig.Runtime.RunRoundAsync(round, cancellation.Token);
		await rig.Runtime.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), "the resolve round to start");
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
		Assert.False(Directory.Exists(rig.Runtime.SandboxWorkFolder(RoundExecutorRig.CardId, SandboxRole.Resolve)), "The resolve sandbox goes when its round is stopped.");
	}
}
