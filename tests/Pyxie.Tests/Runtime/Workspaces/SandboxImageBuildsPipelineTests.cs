using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>The pipeline's way to a project's image (<see cref="SandboxImageBuilds.EnsureAsync"/>): a round whose image is there reports nothing, and a round whose image has to be built reports the build once it begins, shows it as the pipeline's while it runs, and ends it however the build ends. The build's own outcome, and the exception a cancelled or failing engine throws, reach the round unchanged.</summary>
public sealed class SandboxImageBuildsPipelineTests
{
	private const string FailureMessage = "The project's sandbox image could not be built.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheImageIsThere_ReportsNoBuildAndAsksNotToRebuild()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.ImagePresent = true;
		Task nextEnd = rig.Builds.NextEndAsync();

		ServiceResult<string> result = await SandboxImageBuildsRig.WithinAsync(rig.EnsureAsync(Token));

		Assert.Equal(ImageBuildingSandboxes.Tag, ResultAssert.Succeeded(result));
		SandboxImageRequest request = Assert.Single(rig.Engine.Requests);
		Assert.Equal(new SandboxImageRequest(rig.ProjectKey, SandboxImageBuildsRig.Dockerfile, Rebuild: false, rig.LogPath), request with { BuildStarting = null });
		Assert.Null(rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.Empty(rig.Snapshots);
		Assert.False(nextEnd.IsCompleted, "No build ended.");
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheImageIsThere_LeavesTheLastBuildAsItWas()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.TryStart();
		await rig.EndedAsync();
		SandboxImageBuild? last = rig.Latest;
		int changes = rig.Snapshots.Count;
		rig.Engine.ImagePresent = true;

		ResultAssert.Succeeded(await SandboxImageBuildsRig.WithinAsync(rig.EnsureAsync(Token)));

		Assert.NotNull(last);
		Assert.Same(last, rig.Latest);
		Assert.Equal(changes, rig.Snapshots.Count);
		Assert.Equal(1, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheImageWasBuiltWhileItWaited_ReportsNoBuild()
	{
		await using SandboxImageBuildsRig rig = new();
		await rig.Engine.OccupyAsync(Token);
		Task<ServiceResult<string>> ensure = rig.EnsureAsync(Token);
		await rig.QueuedAsync();

		rig.Engine.ImagePresent = true;
		rig.Engine.Vacate();

		Assert.Equal(ImageBuildingSandboxes.Tag, ResultAssert.Succeeded(await SandboxImageBuildsRig.WithinAsync(ensure)));
		Assert.Null(rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.Empty(rig.Snapshots);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheImageMustBeBuilt_ShowsThePipelinesBuildWhileItRunsAndEndsItSucceeded()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.Clock.Tick();

		Task<ServiceResult<string>> ensure = rig.EnsureAsync(Token);
		SandboxImageRequest request = await rig.BeganAsync();

		SandboxImageBuild? running = rig.Latest;
		Assert.NotNull(running);
		Assert.Equal(running, rig.Builds.Building);
		Assert.Equal(new SandboxImageBuild(running.Id, rig.ProjectKey, SandboxImageBuildsRig.Dockerfile, SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Running, rig.Clock.UtcNow, EndedUtc: null, Tag: null, Error: null, rig.LogPath, LogTail: null), running);
		Assert.Equal(new SandboxImageRequest(rig.ProjectKey, SandboxImageBuildsRig.Dockerfile, Rebuild: false, rig.LogPath), request with { BuildStarting = null });
		Assert.Null(rig.Builds.PersonBuild);
		Assert.False(rig.Builds.CanStart, "A person cannot ask for a build while the pipeline's runs.");
		Assert.False(rig.TryStart(), "A person's build is refused while the pipeline's runs.");
		Assert.Single(rig.Engine.Requests);
		Assert.False(ensure.IsCompleted, "The round waits for its image.");

		DateTime ended = rig.Clock.Tick();
		rig.Engine.Finish();
		ServiceResult<string> result = await SandboxImageBuildsRig.WithinAsync(ensure);

		Assert.Equal(ImageBuildingSandboxes.Tag, ResultAssert.Succeeded(result));
		Assert.Equal(running with { State = SandboxImageBuildState.Succeeded, EndedUtc = ended, Tag = ImageBuildingSandboxes.Tag }, rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.True(rig.Builds.CanStart, "The slot is free once the build ended.");
		Assert.Equal(1, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheBuildFails_RecordsItFailedWithTheLogTailAndReturnsTheFailure()
	{
		await using SandboxImageBuildsRig rig = new();
		string[] lines = [.. Enumerable.Range(1, 30).Select(number => $"#{number} RUN step {number}")];
		rig.Engine.Result = ServiceResult<string>.Failure(FailureMessage);
		rig.Engine.LogLines = lines;

		ServiceResult<string> result = await SandboxImageBuildsRig.WithinAsync(rig.EnsureAsync(Token));

		Assert.Equal(FailureMessage, ResultAssert.Failed(result));
		SandboxImageBuild? build = rig.Latest;
		Assert.NotNull(build);
		Assert.Equal((SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Failed, FailureMessage, string.Join('\n', lines[^20..])), (build.RequestedBy, build.State, build.Error, build.LogTail));
		Assert.Null(build.Tag);
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheEngineStopsAnsweringDuringTheBuild_RecordsItFailedAndTheExceptionReachesTheRound()
	{
		const string stopped = "Docker stopped answering.";
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.UnavailableWhileBuilding = stopped;

		SandboxUnavailableException thrown = await Assert.ThrowsAsync<SandboxUnavailableException>(() => SandboxImageBuildsRig.WithinAsync(rig.EnsureAsync(Token)));

		Assert.Equal(stopped, thrown.Message);
		SandboxImageBuild? build = rig.Latest;
		Assert.NotNull(build);
		Assert.Equal((SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Failed, stopped), (build.RequestedBy, build.State, build.Error));
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenTheEngineDoesNotAnswerBeforeABuildBegins_ReportsNothingAndTheExceptionReachesTheRound()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.UnavailableWhenAsked = "Docker is not running.";

		await Assert.ThrowsAsync<SandboxUnavailableException>(() => SandboxImageBuildsRig.WithinAsync(rig.EnsureAsync(Token)));

		Assert.Null(rig.Latest);
		Assert.Empty(rig.Snapshots);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenCancelledWhileBuilding_LeavesNoResultOfItsOwnAndTheCancellationReachesTheRound()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.Engine.LogLines = ["#1 RUN not finished"];
		using CancellationTokenSource stopping = new();
		Task<ServiceResult<string>> ensure = rig.EnsureAsync(stopping.Token);
		await rig.BeganAsync();
		Assert.Equal((SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Running), (rig.Latest?.RequestedBy, rig.Latest?.State));

		await stopping.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SandboxImageBuildsRig.WithinAsync(ensure));
		Assert.Null(rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.True(rig.Builds.CanStart, "A cancelled build frees the slot.");
		Assert.Equal<SandboxImageBuildState?>([SandboxImageBuildState.Running, null], [.. rig.Snapshots.Select(snapshot => snapshot.Latest?.State)]);
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhenCancelledWhileWaitingForABuildAhead_ReportsNothingAndTheCancellationReachesTheRound()
	{
		await using SandboxImageBuildsRig rig = new();
		await rig.Engine.OccupyAsync(Token);
		using CancellationTokenSource stopping = new();
		Task<ServiceResult<string>> ensure = rig.EnsureAsync(stopping.Token);
		await rig.QueuedAsync();

		await stopping.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SandboxImageBuildsRig.WithinAsync(ensure));
		Assert.Null(rig.Latest);
		Assert.Empty(rig.Snapshots);
		rig.Engine.Vacate();
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureAsync_WhileAPersonsBuildRuns_WaitsForItAndReportsItsOwnBuildAfterwards()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.TryStart();
		await rig.QueuedAsync();
		await rig.BeganAsync();
		SandboxImageBuild? person = rig.Latest;
		Task<ServiceResult<string>> ensure = rig.EnsureAsync(Token);
		SandboxImageRequest queued = await rig.QueuedAsync();

		Assert.False(queued.Rebuild, "The round never asks for a rebuild.");
		Assert.Same(person, rig.Latest);

		rig.Engine.Finish();
		SandboxImageRequest began = await rig.BeganAsync();

		Assert.False(began.Rebuild, "The round's build is the one that began.");
		Assert.Equal(SandboxImageBuildRequester.Pipeline, rig.Latest?.RequestedBy);
		Assert.Equal(SandboxImageBuildState.Running, rig.Latest?.State);
		Assert.Equal(rig.Latest, rig.Builds.Building);

		rig.Engine.Finish();
		ResultAssert.Succeeded(await SandboxImageBuildsRig.WithinAsync(ensure));
		Assert.Equal(2, rig.Builds.SucceededBuilds);
	}
}
