using System.Globalization;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>
/// A gate round runs the enabled gates in a fresh gate sandbox on the card's last commit, never in the worker's sandbox, and nothing the gates change or commit comes back to the card.
/// </summary>
public sealed class RoundExecutorGateTests
{
	private const string GateFileStatus = "?? made-by-a-gate.txt";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAGateRound_RunsTheEnabledGatesInOrderAndSkipsTheDisabledOnes()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.AddRecordingGateAsync("Build");
		await rig.AddRecordingGateAsync("Disabled", isEnabled: false);
		await rig.AddRecordingGateAsync("Test");
		await rig.WorkAsync();

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(RoundKind.Gate);

		Assert.Equal((RoundResult.Succeeded, "2 gates passed."), (verdict.Result, verdict.Note));
		Assert.Equal(rig.Runtime.Repository?.Id, repository?.Id);
		Assert.Equal(["Build", "Test"], await rig.ReadStartedGatesAsync());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAGateRoundWithNoEnabledGate_PassesWithoutRunningAnything()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.AddRecordingGateAsync("Disabled", isEnabled: false);
		await rig.WorkAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Gate);

		Assert.Equal((RoundResult.Succeeded, "No gates are enabled."), (verdict.Result, verdict.Note));
		Assert.Empty(await rig.ReadStartedGatesAsync());
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ForAGateRound_RunsTheGatesInAFreshSandboxOnTheCardsLastCommit()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "gate.jsonl");
		await rig.Runtime.AddGateAsync(new GateInput("Head", "git", "rev-parse HEAD", TimeoutMinutes: 5, IsEnabled: true));
		await rig.Runtime.AddGateAsync(new GateInput("Status before", "git", "status --porcelain --untracked-files=all", TimeoutMinutes: 5, IsEnabled: true));
		await rig.Runtime.AddGateAsync("Writer", "--record", record, "--write", "made-by-a-gate.txt=left behind");
		await rig.Runtime.AddGateAsync(new GateInput("Status after", "git", "status --porcelain --untracked-files=all", TimeoutMinutes: 5, IsEnabled: true));
		string cardTip = await rig.WorkAsync();

		// What a worker left uncommitted stays in its own sandbox; the gates never see it.
		await File.WriteAllTextAsync(Path.Combine(rig.WorkerRepository, "left-by-the-worker.txt"), "uncommitted", Token);

		(RoundVerdict first, _) = await rig.RunAsync(RoundKind.Gate);
		File.Delete(record);
		(RoundVerdict second, _) = await rig.RunAsync(RoundKind.Gate);

		// Each round's second status lists the file its own Writer gate made; a sandbox kept from the first round would list it in the second round's first status as well.
		Assert.Equal((RoundResult.Succeeded, RoundResult.Succeeded), (first.Result, second.Result));
		string firstLog = await rig.ReadRoundLogAsync(sequence: 2);
		string secondLog = await rig.ReadRoundLogAsync(sequence: 3);
		Assert.Contains(cardTip, firstLog, StringComparison.Ordinal);
		Assert.DoesNotContain("left-by-the-worker.txt", firstLog, StringComparison.Ordinal);
		Assert.Equal(1, LinesOf(firstLog).Count(line => line == GateFileStatus));
		Assert.Equal(1, LinesOf(secondLog).Count(line => line == GateFileStatus));
		Assert.NotEqual(Path.GetFullPath(rig.WorkerRepository), await RoundExecutorRig.ReadRecordedDirectoryAsync(record), StringComparer.OrdinalIgnoreCase);
		Assert.Equal([SandboxRole.Worker, SandboxRole.Gates, SandboxRole.Gates], rig.StartedSandboxes().Select(sandbox => sandbox.Role));
		Assert.False(Directory.Exists(rig.Runtime.SandboxWorkFolder(RoundExecutorRig.CardId, SandboxRole.Gates)), "The gate sandbox goes when its round ends.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(0, RoundResult.Succeeded)]
	[InlineData(1, RoundResult.Failed)]
	public async Task RunAsync_ForAGateRound_NeverChangesTheCardWhateverTheGatesChangeOrCommit(int finalExitCode, RoundResult expected)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		await rig.Runtime.AddGateAsync("Writer", "--write", "README.md=changed by a gate", "--write", "new.txt=untracked");
		await rig.Runtime.AddGateAsync(new GateInput("Committer", "git", "commit --all --message gate-commit", TimeoutMinutes: 5, IsEnabled: true));
		await rig.Runtime.AddGateAsync("Finisher", "--write", "README.md=changed again", "--exit", finalExitCode.ToString(CultureInfo.InvariantCulture));
		string workerCommit = await rig.WorkAsync();

		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Gate);

		Assert.Equal(expected, verdict.Result);
		Assert.Equal(workerCommit, await rig.GetBranchTipAsync());
		Assert.Equal(TempGitRepository.InitialContent, await File.ReadAllTextAsync(Path.Combine(rig.WorkerRepository, "README.md"), Token));
		Assert.False(File.Exists(Path.Combine(rig.WorkerRepository, "new.txt")), "A file a gate created must not reach the worker's sandbox.");
		Assert.Empty(await rig.GetWorkerChangesAsync());
	}

	private static IEnumerable<string> LinesOf(string log)
	{
		return log.Split('\n').Select(line => line.TrimEnd('\r'));
	}
}
