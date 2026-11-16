using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>While the pipeline is stopped no round can be starting a sandbox, so the orchestrator removes what a person's build left as soon as the build succeeded, once for it. A build that failed or was cancelled, or none at all, leaves nothing to remove. The orchestrator wakes when a build ends, so these tests never wait for a poll. What must not happen has nothing to wait for: it is watched for as long as an idle pipeline is, and then a build that succeeds shows that the orchestrator was ready to remove all along.</summary>
public sealed class PipelineOrchestratorImageCleanupWhileStoppedTests
{
	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AStoppedPipeline_RemovesUnusedImagesOnceWhenAPersonsBuildSucceeds()
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		await rig.Harness.StartOrchestratorAsync();
		Assert.Empty(rig.Removals);

		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(1);
		rig.Harness.NotifyCardsChanged();
		await rig.WatchAsync();

		Assert.False(rig.Harness.Controller.IsRunning, "The pipeline was never started.");
		rig.AssertRemovals(1);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AStoppedPipeline_WithNoBuildAtAll_RemovesNothing()
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		await rig.Harness.StartOrchestratorAsync();
		rig.Harness.NotifyCardsChanged();

		await rig.WatchAsync();

		Assert.Empty(rig.Removals);
		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(1);
		rig.AssertRemovals(1);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildState.Failed)]
	[InlineData(SandboxImageBuildState.Cancelled)]
	public async Task AStoppedPipeline_AfterABuildThatDidNotSucceed_RemovesNothingUntilABuildDoes(SandboxImageBuildState outcome)
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		await rig.Harness.StartOrchestratorAsync();

		await rig.BuildAsync(outcome);
		await rig.WatchAsync();

		Assert.Empty(rig.Removals);
		await rig.BuildAsync(SandboxImageBuildState.Succeeded);
		await rig.WaitForRemovalsAsync(1);
		rig.AssertRemovals(1);
	}
}
