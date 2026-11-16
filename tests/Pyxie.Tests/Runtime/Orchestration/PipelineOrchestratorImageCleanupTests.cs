using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Starting the pipeline removes the sandbox images and build cache that nothing needs any more, which earlier builds left. It happens once per start, for the open project and the Dockerfile saved in its settings, and before any round asks for its image, because a round that already found its image must not find it gone when it starts the sandbox.</summary>
public sealed class PipelineOrchestratorImageCleanupTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartingThePipeline_RemovesUnusedImagesOnceForTheOpenProjectAndItsSavedDockerfileBeforeAnyRoundAsksForItsImage()
	{
		await using ImageCleanupRig rig = await ImageCleanupRig.CreateAsync();
		Agent worker = await rig.Harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await rig.Harness.AddCardAsync("Add the feature", worker);
		TaskCompletionSource<int> removalsWhenTheFirstRoundAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		rig.Harness.Sandboxes.EnsureImage = (request, cancellationToken) =>
		{
			removalsWhenTheFirstRoundAsked.TrySetResult(rig.Removals.Count);

			return rig.Engine.EnsureImageAsync(request, cancellationToken);
		};

		await rig.Harness.StartPipelineAsync();
		await rig.Harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await rig.Harness.StopOrchestratorAsync();

		Assert.NotEqual(SandboxDefaults.Dockerfile, rig.Dockerfile);
		rig.AssertRemovals(1);
		Assert.Equal(1, await removalsWhenTheFirstRoundAsked.Task.WaitAsync(RuntimeTestTimeouts.Wait, Token));
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}
}
