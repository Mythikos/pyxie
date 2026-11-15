using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A sandbox that cannot be made for a reason of the project's own, such as an image that does not build, blocks the round with what went wrong, its first line as the note and all of it as the report. A container engine that does not answer is not the card's fault: the executor throws <see cref="SandboxUnavailableException"/> for the pipeline to give the round back.</summary>
public sealed class RoundExecutorSandboxFailureTests
{
	private const string ImageError = "The project's sandbox image did not build.\nStep 3 of 7 failed: apt-get exited with code 100.";
	private const string StartError = "The sandbox could not start: its image is broken.";
	private const string UnavailableError = "Docker is not running.";

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenTheProjectsImageDoesNotBuild_IsBlockedWithTheFirstLineAsTheNoteAndTheWholeMessageAsTheReport()
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "worker.jsonl");
		AgentSnapshot worker = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--record", record);
		rig.Runtime.Sandboxes.ImageError = ImageError;

		(RoundVerdict verdict, Repository? repository) = await rig.RunAsync(RoundKind.Work, worker);

		Assert.Equal((RoundResult.Blocked, "The project's sandbox image did not build.", ImageError), (verdict.Result, verdict.Note, verdict.Report));
		Assert.Equal(rig.Runtime.Repository?.Id, repository?.Id);
		Assert.False(File.Exists(record), "The worker must not start without a sandbox.");
		Assert.Empty(rig.StartedSandboxes());
		Assert.False(await rig.BranchExistsAsync(), "Nothing ran, so the card's branch is not made.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	public async Task RunAsync_WhenTheSandboxCannotStart_IsBlockedWithItsMessageAndLeavesTheCardBranchAlone(RoundKind kind)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "agent.jsonl");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record, "--write", "late.txt=never", "--outcome", kind == RoundKind.Review ? "approved" : "completed");
		await rig.AddRecordingGateAsync("Check");
		string cardTip = await rig.WorkAsync();
		rig.Runtime.Sandboxes.StartFailure = new SandboxException(StartError);

		(RoundVerdict verdict, _) = await rig.RunAsync(kind, kind == RoundKind.Gate ? null : agent);

		Assert.Equal((RoundResult.Blocked, StartError, StartError), (verdict.Result, verdict.Note, verdict.Report));
		Assert.False(File.Exists(record), "No agent may run without a sandbox.");
		Assert.Empty(await rig.ReadStartedGatesAsync());
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	public async Task RunAsync_WhenTheContainerEngineDoesNotAnswer_ThrowsSandboxUnavailableExceptionAndChangesNothing(RoundKind kind)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string record = Path.Combine(rig.MarkerFolder, "agent.jsonl");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--record", record, "--write", "late.txt=never", "--outcome", kind == RoundKind.Review ? "approved" : "completed");
		await rig.AddRecordingGateAsync("Check");
		string cardTip = await rig.WorkAsync();
		rig.Runtime.Sandboxes.StartFailure = new SandboxUnavailableException(UnavailableError);

		SandboxUnavailableException thrown = await Assert.ThrowsAsync<SandboxUnavailableException>(() => rig.RunAsync(kind, kind == RoundKind.Gate ? null : agent));

		Assert.Equal(UnavailableError, thrown.Message);
		Assert.False(File.Exists(record), "No agent may run without a sandbox.");
		Assert.Empty(await rig.ReadStartedGatesAsync());
		Assert.Equal(cardTip, await rig.GetBranchTipAsync());
	}
}
