using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>When removing unused images does not happen, the orchestrator tries again once the next build ends, and no sooner: the sandboxes answer <see langword="false"/> when a build was running, so nothing was removed and the builds are not counted as cleaned up, and a failure (Docker not answering) is logged and not repeated until a build succeeds again. A removal that ran is not repeated for builds that did not succeed. What must not happen has nothing to wait for, so it is watched for as long as an idle pipeline is (see <see cref="PipelineOrchestratorImageCleanupWhileStoppedTests"/>).</summary>
public sealed class PipelineOrchestratorImageCleanupRetryTests
{
	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ARemovalThatFoundABuildRunning_IsTriedAgainWhenTheNextBuildEndsEvenIfThatBuildFailed()
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		rig.Harness.Sandboxes.RemoveUnusedImagesResult = false;
		await rig.Harness.StartOrchestratorAsync();

		// The orchestrator settles first: a build that ended while it was still starting up would make it try twice in a row.
		await rig.WatchAsync();
		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(1);
		await rig.WatchAsync();
		rig.AssertRemovals(1);
		rig.Harness.Sandboxes.RemoveUnusedImagesResult = true;
		await rig.BuildAsync(SandboxImageBuildState.Failed);
		await rig.WaitForRemovalsAsync(2);
		await rig.WatchAsync();

		rig.AssertRemovals(2);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ARemovalThatRan_IsNotRepeatedForBuildsThatDidNotSucceedAndIsDoneAgainForTheNextSuccess()
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		await rig.Harness.StartOrchestratorAsync();
		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(1);

		await rig.BuildAsync(SandboxImageBuildState.Failed);
		await rig.BuildAsync(SandboxImageBuildState.Cancelled);
		rig.Harness.NotifyCardsChanged();
		await rig.WatchAsync();

		rig.AssertRemovals(1);
		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(2);
		rig.AssertRemovals(2);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ARemovalThatFails_IsNotRepeatedUntilAnotherBuildSucceedsAndTheOrchestratorKeepsWorking()
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		rig.Harness.Sandboxes.RemoveUnusedImagesFailure = new SandboxUnavailableException("Docker stopped answering.");
		await rig.Harness.StartOrchestratorAsync();

		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(1);
		rig.Harness.NotifyCardsChanged();
		await rig.WatchAsync();

		rig.AssertRemovals(1);
		rig.Harness.Sandboxes.RemoveUnusedImagesFailure = null;
		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(2);
		rig.AssertRemovals(2);
	}
}
