using System.Text.Json;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A work round runs the worker in the card's kept worker sandbox, commits what it left there, and takes the commits back onto the card's branch in the hub, never onto the target branch. A round that does not succeed takes nothing back, and what the worker left stays in its sandbox for the next attempt.</summary>
public sealed class RoundExecutorWorkTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAWorkRound_CommitsTheWorkInTheSandboxAndTakesItBackOntoTheCardBranchInTheHub()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string initialMain = await rig.Hub.GetHashAsync("main");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=hello", "--write", "docs/notes.txt=nested", "--note", "added it");

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(RoundKind.Work, worker);

		Assert.Equal((RoundResult.Succeeded, "added it"), (verdict.Result, verdict.Note));
		Assert.Equal(rig.Runtime.Repository?.Id, repository?.Id);
		Assert.Equal("hello", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "feature.txt"));
		Assert.Equal("nested", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "docs/notes.txt"));
		Assert.Equal(initialMain, await rig.Hub.GetHashAsync($"{RoundExecutorRig.Branch}~1"));
		Assert.Equal(initialMain, await rig.Hub.GetHashAsync("main"));
		Assert.Null(await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal(string.Empty, await rig.Hub.RunGitAsync("for-each-ref", "refs/pyxie"));
		Assert.Empty(await rig.GetWorkerChangesAsync());
		SandboxSpec sandbox = Assert.Single(rig.StartedSandboxes());
		Assert.Equal((SandboxRole.Worker, RoundExecutorRig.CardId), (sandbox.Role, sandbox.CardId));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAWorkRound_CommitsWithTheCardNameAndNumberAndLeavesPyxiesFolderOutOfTheCommit()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=hello");

		await rig.RunAsync(RoundKind.Work, worker);

		string message = await rig.Hub.RunGitAsync("log", "-1", "--format=%B", RoundExecutorRig.Branch);
		string[] files = (await rig.Hub.RunGitAsync("ls-tree", "-r", "--name-only", RoundExecutorRig.Branch)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.StartsWith(RoundExecutorRig.CardName, message, StringComparison.Ordinal);
		Assert.Contains("Card 1, attempt 1.", message, StringComparison.Ordinal);
		Assert.Equal(["README.md", "feature.txt"], files.Order(StringComparer.Ordinal));
		Assert.True(File.Exists(Path.Combine(rig.WorkerRepository, PipelineContract.ResultFile)), "The worker's result file is still in its sandbox; it is only left out of the commit.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenTheWorkerChangesNothing_StartsTheCardBranchAtTheTargetWithoutACommit()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string initialMain = await rig.Hub.GetHashAsync("main");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other);

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Work, worker);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(initialMain, await rig.GetBranchTipAsync());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForALaterWorkRound_CommitsOnTopOfWhatTheCardAlreadyHas()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		AgentSnapshot first = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=first");
		AgentSnapshot second = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=second", "--write", "extra.txt=more");

		await rig.RunAsync(RoundKind.Work, first);
		string firstTip = await rig.GetBranchTipAsync();
		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Work, second);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(firstTip, await rig.Hub.GetHashAsync($"{RoundExecutorRig.Branch}~1"));
		Assert.Equal("2", await rig.Hub.RunGitAsync("rev-list", "--count", $"main..{RoundExecutorRig.Branch}"));
		Assert.Equal("second", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "feature.txt"));
		Assert.Equal("more", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "extra.txt"));
		Assert.Equal(2, rig.StartedSandboxes().Count(sandbox => sandbox.Role == SandboxRole.Worker));
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData("errored", RoundResult.Errored)]
	[InlineData("blocked", RoundResult.Blocked)]
	public async Task RunAsync_WhenTheWorkerDoesNotSucceed_ReturnsItsVerdictTakesNothingBackAndKeepsWhatItLeftForTheNextAttempt(string outcome, RoundResult expected)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		AgentSnapshot stuck = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "partial.txt=half done", "--outcome", outcome, "--note", "could not finish");
		AgentSnapshot finisher = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=hello");

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Work, stuck);

		Assert.Equal((expected, "could not finish"), (verdict.Result, verdict.Note));
		Assert.False(await rig.BranchExistsAsync(), "Nothing comes back from a round that did not succeed, so the card's branch is not made.");
		Assert.Equal(["?? partial.txt"], await rig.GetWorkerChangesAsync());

		(RoundVerdict next, _) = await rig.RunAsync(RoundKind.Work, finisher);

		Assert.Equal(RoundResult.Succeeded, next.Result);
		Assert.Equal("half done", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "partial.txt"));
		Assert.Equal("hello", await rig.Hub.ReadFileAtAsync(RoundExecutorRig.Branch, "feature.txt"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenAHookInTheSandboxRefusesTheCommit_IsErroredWithTheHooksReasonAndTakesNothingBack()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string firstTip = await rig.WorkAsync();
		await rig.InstallWorkerHookAsync("pre-commit", "echo 'commit refused by the hook' >&2\nexit 1");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "more.txt=refused");

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Work, worker);

		Assert.Equal((RoundResult.Errored, "Committing the worker's changes failed: commit refused by the hook"), (verdict.Result, verdict.Note));
		Assert.Equal(firstTip, await rig.GetBranchTipAsync());
		Assert.Equal("refused", await File.ReadAllTextAsync(Path.Combine(rig.WorkerRepository, "more.txt"), Token));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenTheCardBranchMovesInTheHubWhileTheRoundRuns_IsErroredAndLeavesTheBranchWhereItMoved()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.WorkAsync();
		string main = await rig.Hub.GetHashAsync("main");

		// The hook runs while Pyxie commits the worker's changes, after the round started from the card's tip and before the work is taken back: the branch moves in between.
		await rig.InstallWorkerHookAsync("pre-commit", $"git --git-dir=\"{rig.Hub.GitFolder.Replace('\\', '/')}\" update-ref refs/heads/{RoundExecutorRig.Branch} {main}\nexit 0");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "more.txt=late");

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Work, worker);

		Assert.Equal((RoundResult.Errored, $"{RoundExecutorRig.Branch} moved in the repository while the round ran."), (verdict.Result, verdict.Note));
		Assert.Equal(main, await rig.GetBranchTipAsync());
		Assert.Equal(string.Empty, await rig.Hub.RunGitAsync("for-each-ref", "refs/pyxie"));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_RunsTheWorkerInTheCardsKeptSandboxWithTheBriefingWrittenAndThePromptOnStandardInput()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "worker.jsonl");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--record", record);

		await rig.RunAsync(RoundKind.Work, worker);

		using JsonDocument run = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(record, Token)));
		Assert.Equal(Path.GetFullPath(rig.WorkerRepository), await RoundExecutorRig.ReadRecordedDirectoryAsync(record), ignoreCase: OperatingSystem.IsWindows());
		Assert.Equal(PipelineContract.Prompt, run.RootElement.GetProperty("input").GetString()?.Trim());
		Assert.Equal("Briefing of round 1", await File.ReadAllTextAsync(Path.Combine(rig.WorkerRepository, PipelineContract.BriefingFile), Token));
		Assert.True(Assert.Single(rig.StartedSandboxes()).Keep, "A worker's sandbox is kept between the card's work rounds.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_KeepsTheWorkersConsoleOutputInTheRoundLog()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--outcome", "completed");

		await rig.RunAsync(RoundKind.Work, worker);

		Assert.Contains("fake harness: completed", await rig.ReadRoundLogAsync(sequence: 1), StringComparison.Ordinal);
	}
}
