using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>When the pipeline starts it removes the sandboxes and branches of cards that no longer need them, which a stopped Pyxie leaves behind. It removes exactly the card branches (<c>pyxie/card-N</c> with a number) of cards that are gone, and never another branch.</summary>
public sealed class PipelineOrchestratorAbandonedWorkspaceTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingThePipeline_RemovesTheSandboxesAndBranchOfACardThatIsGoneAndKeepsEveryOtherBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		Repository connected = await harness.ConnectRepositoryAsync(source);
		const int goneCardId = 777;
		string goneBranch = $"pyxie/card-{goneCardId}";
		string goneWorker = harness.WorkerRepository(goneCardId);
		string goneHostFolder = WorkspaceLayout.CardSandboxesFolder(harness.ProjectFolder, goneCardId);
		Directory.CreateDirectory(goneWorker);
		Directory.CreateDirectory(goneHostFolder);
		await harness.Clone.CreateBranchAsync(goneBranch);
		await harness.Clone.CreateBranchAsync("feature/keep");
		await harness.Clone.CreateBranchAsync("pyxie/card-notanumber");
		await harness.Clone.CreateBranchAsync("pyxie/card-12x");

		await harness.StartPipelineAsync();

		// The test only looks at files while the pipeline works, because git can fail for a moment while the pipeline rewrites the repository's config. The branch is a loose ref and the last thing removed.
		string goneBranchRef = Path.Combine(harness.Clone.GitFolder, "refs", "heads", "pyxie", $"card-{goneCardId}");
		string goneConfigLock = Path.Combine(harness.Clone.GitFolder, "config.lock");
		await harness.WaitUntilAsync(() => Task.FromResult(!Directory.Exists(goneWorker) && !Directory.Exists(goneHostFolder) && !File.Exists(goneBranchRef) && !File.Exists(goneConfigLock)), $"the sandboxes and branch of the missing card {goneCardId} to be removed", RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		foreach (string kept in new[] { "feature/keep", "pyxie/card-notanumber", "pyxie/card-12x", TempGitRepository.MainBranch })
		{
			Assert.True(await harness.Clone.Git.BranchExistsAsync(harness.Clone.Folder, kept, Token), $"The branch {kept} is not a card's and must stay.");
		}

	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingAgain_KeepsTheWorkerSandboxOfAnInterruptedCardAndTheWorkLeftInIt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		Repository connected = await harness.ConnectRepositoryAsync(source);
		string started = Path.Combine(harness.ScriptsFolder, "started.jsonl");
		string script = await harness.WriteScriptAsync(["--record", started, "--sleep", "4000"], ["--write", "feature.txt=hello"]);
		Agent worker = await harness.AddAgentAsync(FakeHarnessAgent.ScriptInput("Worker", HarnessKind.Other, canWork: true, canReview: false, script));
		WorkCard card = await harness.AddCardAsync("Interrupted card", worker);
		await harness.StartPipelineAsync();
		await harness.WaitUntilAsync(() => Task.FromResult(File.Exists(started)), "the worker to start", RuntimeTestTimeouts.Wait);
		harness.Controller.Stop();
		await harness.WaitForCardAsync(card.Id, board => board.History is { WorkRounds: 1, IsRunning: false }, $"card {card.Id}'s round to be interrupted", RuntimeTestTimeouts.Wait);

		// What the interrupted worker would have left in its sandbox.
		string workerRepository = harness.WorkerRepository(card.Id);
		await File.WriteAllTextAsync(Path.Combine(workerRepository, "partial.txt"), "half done", Token);
		harness.Controller.Start();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		Assert.Equal("half done", await harness.Clone.ReadFileAtAsync("main", "partial.txt"));
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}
}
