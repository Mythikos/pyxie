using System.Diagnostics;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Rules;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;
using Xunit.Sdk;

namespace Pyxie.Tests.Runtime;

/// <summary>Proves the shared runtime test support: the fake harness agent helpers, <see cref="TempGitRepository"/>, and <see cref="RuntimeHarness"/>, including a card that runs the whole pipeline against a real repository. Later runtime tests rely on these helpers, so each behavior they promise is checked here.</summary>
public sealed class SupportSmokeTests
{
	private const string DoneNote = "all done";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void FakeHarnessAgent_DllPath_IsTheFakeHarnessWithItsRuntimeConfigBesideIt()
	{
		string dll = FakeHarnessAgent.DllPath;

		Assert.Equal("Pyxie.FakeHarness.dll", Path.GetFileName(dll));
		Assert.True(File.Exists(dll), $"The fake harness dll {dll} must exist.");
		Assert.True(File.Exists(Path.ChangeExtension(dll, ".runtimeconfig.json")), "dotnet cannot run the dll without its .runtimeconfig.json beside it.");
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode)]
	[InlineData(HarnessKind.Codex)]
	[InlineData(HarnessKind.DeepSeek)]
	[InlineData(HarnessKind.Other)]
	public async Task FakeHarnessAgent_Snapshot_RunsThroughTheHarnessRunnerAndWritesItsResultAndFiles(HarnessKind harness)
	{
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Fake", harness, "--outcome", "completed", "--note", DoneNote, "--write", "output.txt=written by the fake");

		await WithWorkspaceAsync(async workspace =>
		{
			RoundVerdict verdict = await RunWorkRoundAsync(agent, workspace);

			Assert.Equal(RoundResult.Succeeded, verdict.Result);
			Assert.Equal(DoneNote, verdict.Note);
			Assert.Equal("written by the fake", await File.ReadAllTextAsync(Path.Combine(workspace, "output.txt"), Token));
			Assert.True(File.Exists(Path.Combine(workspace, PipelineContract.ResultFile)), "The fake harness must write the result file.");
		});
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode)]
	[InlineData(HarnessKind.Codex)]
	[InlineData(HarnessKind.DeepSeek)]
	[InlineData(HarnessKind.Other)]
	public async Task FakeHarnessAgent_Snapshot_ForStandardInputHarnesses_ReceivesThePromptOnStandardInput(HarnessKind harness)
	{
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Fake", harness, "--require-prompt", PipelineContract.Prompt);

		await WithWorkspaceAsync(async workspace =>
		{
			RoundVerdict verdict = await RunWorkRoundAsync(agent, workspace);

			Assert.True(verdict.Result == RoundResult.Succeeded, $"The fake harness did not get the prompt on standard input: {verdict.Note} {verdict.Report}");
		});
	}

	[Theory]
	[InlineData("plain")]
	[InlineData("two words")]
	[InlineData("say \"hi\" now")]
	[InlineData("\"already quoted\"")]
	[InlineData("C:\\folder with space\\")]
	[InlineData("backslashes before a quote \\\\\"")]
	[InlineData("tab\tseparated")]
	[InlineData("")]
	public void FakeHarnessAgent_Arguments_GiveBackExactlyTheArgumentsTheRuntimeSplits(string argument)
	{
		string arguments = FakeHarnessAgent.Arguments("--note", argument);

		Assert.Equal([FakeHarnessAgent.DllPath, "--note", argument], CommandLine.Split(arguments));
	}

	[Fact]
	public async Task FakeHarnessAgent_WriteScriptAsync_GivesEachRunItsOwnLineAndRepeatsTheLastOne()
	{
		await WithWorkspaceAsync(async workspace =>
		{
			string script = await FakeHarnessAgent.WriteScriptAsync(workspace, ["--outcome", "errored", "--note", "first run fails"], ["--outcome", "completed", "--note", "second run works"]);
			AgentSnapshot agent = FakeHarnessAgent.ScriptSnapshot("Scripted", HarnessKind.Other, script);

			RoundVerdict first = await RunWorkRoundAsync(agent, workspace);
			RoundVerdict second = await RunWorkRoundAsync(agent, workspace);
			RoundVerdict third = await RunWorkRoundAsync(agent, workspace);

			Assert.Equal((RoundResult.Errored, "first run fails"), (first.Result, first.Note));
			Assert.Equal((RoundResult.Succeeded, "second run works"), (second.Result, second.Note));
			Assert.Equal((RoundResult.Succeeded, "second run works"), (third.Result, third.Note));
		});
	}

	[Fact]
	public async Task FakeHarnessAgent_WriteScriptAsync_RefusesAnArgumentWithAQuoteBecauseAScriptLineCannotExpressIt()
	{
		await WithWorkspaceAsync(async workspace =>
		{
			await Assert.ThrowsAsync<ArgumentException>(() => FakeHarnessAgent.WriteScriptAsync(workspace, ["--note", "say \"hi\""]));
		});
	}

	[Fact]
	public async Task TempGitRepository_CreateAsync_HasOneInitialCommitOnMainAndACleanWorkingTree()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		Assert.Equal("main", await repository.GetCurrentBranchAsync());
		Assert.Equal("1", await repository.RunGitAsync("rev-list", "--count", "main"));
		Assert.Equal(TempGitRepository.InitialContent, await repository.ReadFileAtAsync("main", TempGitRepository.InitialFile));
		Assert.Equal(string.Empty, await repository.RunGitAsync("status", "--porcelain"));
		Assert.Equal(TempGitRepository.AuthorName, await repository.RunGitAsync("log", "-1", "--format=%an"));
	}

	[Fact]
	public async Task TempGitRepository_Helpers_BranchCommitAndReadFilesAtEachRevision()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync("main");

		await repository.CreateBranchAsync("feature");
		await repository.CheckoutAsync("feature");
		string featureTip = await repository.CommitFileAsync("docs/notes.txt", "on the branch");
		await repository.CheckoutAsync("main");

		Assert.Equal("on the branch", await repository.ReadFileAtAsync("feature", "docs/notes.txt"));
		Assert.Null(await repository.ReadFileAtAsync("main", "docs/notes.txt"));
		Assert.Equal(featureTip, await repository.GetHashAsync("feature"));
		Assert.Equal(initial, await repository.GetHashAsync("main"));
		Assert.Equal("main", await repository.GetCurrentBranchAsync());
		Assert.False(File.Exists(Path.Combine(repository.Folder, "docs", "notes.txt")), "The working tree is on main, which does not have the file.");
	}

	[Fact]
	public async Task TempGitRepository_RunGitAsync_FailsTheTestWithGitsOutputWhenTheCommandFails()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		XunitException failure = await Assert.ThrowsAnyAsync<XunitException>(() => repository.RunGitAsync("rev-parse", "--verify", "no-such-branch"));
		ProcessResult tried = await repository.TryRunGitAsync("rev-parse", "--verify", "no-such-branch");

		Assert.Contains("no-such-branch", failure.Message, StringComparison.Ordinal);
		Assert.False(tried.Succeeded, "TryRunGitAsync reports a failing command without failing the test.");
	}

	[Fact]
	public async Task TempGitRepository_DisposeAsync_DeletesTheFolderIncludingGitsReadOnlyFiles()
	{
		TempGitRepository repository = await TempGitRepository.CreateAsync();
		string folder = repository.Folder;
		Assert.True(Directory.Exists(folder), "The repository folder exists until it is disposed.");

		await repository.DisposeAsync();

		Assert.False(Directory.Exists(folder), "Disposing the repository must delete its folder.");
	}

	[Fact]
	public async Task TempGitRepository_AttachAsync_ViewsAnExistingRepositoryWithTheSameLocalSettingsAndLeavesItsFolderWhenDisposed()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-attach");
		string folder = scratch.Combine("clone");
		await source.RunGitAsync("clone", "--quiet", "--no-hardlinks", "--", source.Folder, folder);

		TempGitRepository attached = await TempGitRepository.AttachAsync(folder);

		Assert.Equal(folder, attached.Folder);
		Assert.Equal(TempGitRepository.AuthorName, await attached.RunGitAsync("config", "--local", "user.name"));
		Assert.Equal("false", await attached.RunGitAsync("config", "--local", "core.autocrlf"));
		Assert.Equal("false", await attached.RunGitAsync("config", "--local", "commit.gpgsign"));
		Assert.Equal(TempGitRepository.InitialContent, await attached.ReadFileAtAsync("main", TempGitRepository.InitialFile));
		await attached.DisposeAsync();
		Assert.True(Directory.Exists(folder), "A repository that was attached belongs to someone else, so disposing the view must not delete it.");
	}

	[Fact]
	public async Task RuntimeHarness_Clone_BeforeARepositoryIsConnected_Throws()
	{
		await using RuntimeHarness harness = new();

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => harness.Clone);

		Assert.Contains(nameof(RuntimeHarness.ConnectRepositoryAsync), failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RuntimeHarness_ConnectRepositoryAsync_ImportsAClonePyxieWorksInAndLeavesTheSourceAlone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		SourceSnapshot before = await SourceSnapshot.CaptureAsync(source);

		Repository connected = await harness.ConnectRepositoryAsync(source);

		await before.AssertUnchangedAsync(source);
		Assert.Equal(WorkspaceLayout.HubPath(harness.ProjectFolder), connected.Path);
		Assert.Equal(connected.Path, harness.Clone.Folder);
		Assert.False(string.Equals(Path.GetFullPath(source.Folder), Path.GetFullPath(harness.Clone.Folder), StringComparison.OrdinalIgnoreCase), "The clone is not the folder that was imported.");
		Assert.Equal(await source.GetHashAsync("main"), await harness.Clone.GetHashAsync("main"));
		Assert.Equal(TempGitRepository.InitialContent, await harness.Clone.ReadFileAtAsync("main", TempGitRepository.InitialFile));
	}

	[Fact]
	public async Task RuntimeHarness_RunsACardFromTheQueueToCompleteAndMergesItsChangeIntoTheClonesMain()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		string initialMain = await source.GetHashAsync("main");
		Repository connected = await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello", "--note", "added the feature");
		WorkCard card = await harness.AddCardAsync("Add the feature", worker);

		await harness.StartPipelineAsync();
		BoardCard finished = await harness.WaitForLaneAsync(card.Id, CardLane.Complete);

		Assert.Equal("main", connected.TargetBranch);
		Assert.Equal(CardState.Ready, finished.Card.State);
		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", string.Join(',', rounds.Select(round => $"{round.Kind}:{round.Outcome}")));
		Assert.Equal(PipelineRules.NoGatesNote, rounds[1].Note);
		Assert.Equal(PipelineRules.NoReviewerNote, rounds[2].Note);
		Assert.Equal(("Worker", FakeHarnessAgent.Command), (rounds[0].Agent?.Name, rounds[0].Agent?.Command));
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
		string[] mainTip = (await harness.Clone.RunGitAsync("rev-list", "--parents", "-n", "1", "main")).Split(' ');
		Assert.Equal(3, mainTip.Length);
		Assert.Contains(initialMain, mainTip[1..]);
		await harness.WaitUntilAsync(async () => !await harness.Clone.Git.BranchExistsAsync(harness.Clone.Folder, PipelineContract.BranchName(card.Id), Token), "the merged card's branch to be removed");
	}

	[Fact]
	public async Task RuntimeHarness_RunsGatesAndAReviewerWhenTheTestAddsThem()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		await harness.AddGateAsync("Check", "--exit", "0");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent reviewer = await harness.AddReviewerAsync("Reviewer", "--outcome", "approved");
		WorkCard card = await harness.AddCardAsync("Reviewed feature", worker, reviewer);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal("Work:Completed,Gate:Passed,Review:Approved,Merge:Merged", string.Join(',', rounds.Select(round => $"{round.Kind}:{round.Outcome}")));
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}

	[Fact]
	public async Task RuntimeHarness_ScriptedReviewerRejectsOnceAndThenApprovesSoTheCardGoesBackToWork()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		string script = await harness.WriteScriptAsync(["--outcome", "rejected", "--note", "add a test"], ["--outcome", "approved"]);
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		Agent reviewer = await harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Reviewer", HarnessKind.Other, canWork: false, canReview: true, script));
		WorkCard card = await harness.AddCardAsync("Scripted review", worker, reviewer);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal(
			"Work:Completed,Gate:Skipped,Review:Rejected,Work:Completed,Gate:Skipped,Review:Approved,Merge:Merged",
			string.Join(',', rounds.Select(round => $"{round.Kind}:{round.Outcome}")));
		Assert.Equal("add a test", rounds[2].Note);
	}

	[Fact]
	public async Task RuntimeHarness_WaitForStoppedAsync_ReturnsACardWhoseAgentReportedBlocked()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker", "--outcome", "blocked", "--note", "the task is unclear");
		WorkCard card = await harness.AddCardAsync("Unclear task", worker);

		await harness.StartPipelineAsync();
		BoardCard stopped = await harness.WaitForStoppedAsync(card.Id);

		Assert.Equal(CardLane.Queue, stopped.Card.Lane);
		Assert.Contains("the task is unclear", stopped.History.StopReason, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RuntimeHarness_StartPipelineAsync_AfterTheOrchestratorWasShutDown_RefusesBecauseItCannotRestart()
	{
		await using RuntimeHarness harness = new();
		await harness.StartOrchestratorAsync();
		await harness.StopOrchestratorAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(harness.StartPipelineAsync);
		Assert.False(harness.Controller.IsRunning, "A refused start must leave the pipeline stopped.");
	}

	[Fact]
	public async Task RuntimeHarness_WaitForLaneAsync_WhenTheCardNeverGetsThere_FailsWithTheBoardState()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker");
		WorkCard card = await harness.AddCardAsync("Waiting card", worker);

		XunitException failure = await Assert.ThrowsAnyAsync<XunitException>(() => harness.WaitForLaneAsync(card.Id, CardLane.Complete, TimeSpan.FromMilliseconds(400)));

		Assert.Contains($"card {card.Id} to reach Complete", failure.Message, StringComparison.Ordinal);
		Assert.Contains("Pipeline stopped", failure.Message, StringComparison.Ordinal);
		Assert.Contains($"Card {card.Id} 'Waiting card': Queue, Ready", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RuntimeHarness_DisposeAsync_WhileAnAgentIsRunning_KillsItAndDeletesEverything()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		RuntimeHarness harness = new();
		string started = Path.Combine(harness.ScriptsFolder, "started.txt");
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Sleeper", "--record", started, "--sleep", "120000");
		WorkCard card = await harness.AddCardAsync("Long task", worker);
		await harness.StartPipelineAsync();
		await harness.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), $"the agent of card {card.Id} to start");
		string workspaceRoot = harness.WorkspaceRoot;

		Stopwatch stopwatch = Stopwatch.StartNew();
		await harness.DisposeAsync();

		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Disposing took {stopwatch.Elapsed}: the running agent should have been killed, not waited for.");
		Assert.False(Directory.Exists(workspaceRoot), "Disposing the harness must delete its workspace root, which is only possible once the agent and its sandbox are released.");
	}

	private static async Task WithWorkspaceAsync(Func<string, Task> test)
	{
		string workspace = TempFolder.Create("pyxie-smoke");

		try
		{
			await test(workspace);
		}
		finally
		{
			await TempFolder.DeleteAsync(workspace);
		}
	}

	// Runs the agent the way the pipeline does, through the runtime's own harness runner, in a stand-in sandbox working in the folder.
	private static async Task<RoundVerdict> RunWorkRoundAsync(AgentSnapshot agent, string workspace)
	{
		using ProcessRunner processes = new(TimeProvider.System);
		SandboxSpec spec = new("smoke", LocalSandboxes.Image, "smoke", 1, SandboxRole.Worker, Path.Combine(workspace, ".in"), Path.Combine(workspace, ".out"));
		await using LocalSandbox sandbox = new(processes, spec, workspace);
		HarnessRunner runner = new();
		PipelineWork work = new(CardId: 1, CardName: "Card", RoundId: 1, Sequence: 1, RoundKind.Work, Attempt: 1, agent, "Briefing");

		return await runner.RunAsync(work, sandbox, sandbox.WorkPath, Path.Combine(workspace, "logs", "round.log"), TimeSpan.FromMinutes(1), Token);
	}
}
