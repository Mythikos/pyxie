using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A person's build of a project's image (<see cref="SandboxImageBuilds.TryStart"/>): it runs in the background from the moment it is asked for, one at a time, and ends as succeeded, failed, or cancelled, with the end of the build log kept when it failed. <see cref="ImageBuildingSandboxes"/> builds in the container engine's own order, so the tests look at the builds while one is held and then finished.</summary>
public sealed class SandboxImageBuildsTests
{
	private const string FailureMessage = "The project's sandbox image could not be built.";

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_AsksTheEngineForARebuildOfTheProjectsImageAndShowsItRunningAtOnce()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.Clock.Tick();

		bool started = rig.TryStart();
		SandboxImageRequest request = await rig.BeganAsync();

		Assert.True(started, "A build starts while nothing else builds.");
		SandboxImageBuild? current = rig.Latest;
		Assert.NotNull(current);
		Assert.Equal(new SandboxImageBuild(current.Id, rig.ProjectKey, SandboxImageBuildsRig.Dockerfile, SandboxImageBuildRequester.Person, SandboxImageBuildState.Running, rig.Clock.UtcNow, EndedUtc: null, Tag: null, Error: null, rig.LogPath, LogTail: null), current);
		Assert.Equal(current, rig.Builds.Building);
		Assert.Equal(current, rig.Builds.PersonBuild);
		Assert.False(rig.Builds.CanStart, "No other build can be asked for while one runs.");
		Assert.False(rig.Builds.Running.IsCompleted, "The person's task completes only when the build ends.");
		Assert.Equal(new SandboxImageRequest(rig.ProjectKey, SandboxImageBuildsRig.Dockerfile, Rebuild: true, rig.LogPath), request with { BuildStarting = null });
		Assert.NotNull(request.BuildStarting);

		rig.Engine.Finish();
		await rig.EndedAsync();
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_WhileTheBuildRuns_IsRefusedAndTheEngineIsAskedForNothingMore()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.TryStart();
		await rig.BeganAsync();
		SandboxImageBuild? running = rig.Latest;

		bool second = rig.TryStart();

		Assert.False(second, "A second build would only wait to build the same image again.");
		Assert.Single(rig.Engine.Requests);
		Assert.Same(running, rig.Latest);
		Assert.Same(running, rig.Builds.Building);
		Assert.Same(running, rig.Builds.PersonBuild);

		rig.Engine.Finish();
		await rig.EndedAsync();
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ASucceededBuild_ShowsItsTagAndFreesTheSlot()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.Clock.Tick();
		rig.TryStart();
		await rig.BeganAsync();
		SandboxImageBuild? running = rig.Latest;
		Assert.NotNull(running);
		DateTime ended = rig.Clock.Tick();

		rig.Engine.Finish();
		await rig.EndedAsync();

		Assert.Equal(running with { State = SandboxImageBuildState.Succeeded, EndedUtc = ended, Tag = ImageBuildingSandboxes.Tag }, rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.True(rig.Builds.CanStart, "The slot is free once the build ended.");
		Assert.Equal(1, rig.Builds.SucceededBuilds);
		Assert.Equal([(SandboxImageBuildState.Running, true, true), (SandboxImageBuildState.Running, true, true), (SandboxImageBuildState.Succeeded, false, false)], rig.Snapshots.Select(snapshot => (snapshot.Latest?.State ?? throw new InvalidOperationException("A change was announced with no build."), snapshot.Building is not null, snapshot.PersonBuild is not null)));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_OnceTheBuildEnded_StartsANewBuildThatIsNotTheOldOne()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.TryStart();
		await rig.EndedAsync();
		SandboxImageBuild? first = rig.Latest;
		Assert.NotNull(first);
		rig.Engine.Holding = true;

		bool second = rig.TryStart();
		await rig.BeganAsync();

		Assert.True(second, "A build can be asked for again once the last one ended.");
		SandboxImageBuild? current = rig.Latest;
		Assert.NotNull(current);
		Assert.NotEqual(first.Id, current.Id);
		Assert.Equal(SandboxImageBuildState.Running, current.State);
		Assert.Equal(current, rig.Builds.PersonBuild);

		rig.Engine.Finish();
		await rig.EndedAsync();
		Assert.Equal(2, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AFailedBuild_KeepsTheErrorAndIsNotASuccess()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Result = ServiceResult<string>.Failure(FailureMessage);
		rig.Engine.LogLines = ["#1 RUN fail"];
		DateTime started = rig.Clock.UtcNow;
		rig.Engine.Holding = true;
		rig.TryStart();
		await rig.BeganAsync();
		DateTime ended = rig.Clock.Tick();

		rig.Engine.Finish();
		await rig.EndedAsync();

		SandboxImageBuild? build = rig.Latest;
		Assert.NotNull(build);
		Assert.Equal((SandboxImageBuildState.Failed, started, ended, (string?)null, FailureMessage), (build.State, build.StartedUtc, build.EndedUtc, build.Tag, build.Error));
		Assert.Null(rig.Builds.PersonBuild);
		Assert.True(rig.Builds.CanStart, "A failed build frees the slot.");
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(25, 20)]
	[InlineData(20, 20)]
	[InlineData(3, 3)]
	public async Task AFailedBuild_KeepsTheLastTwentyLinesOfTheLog(int linesWritten, int linesKept)
	{
		await using SandboxImageBuildsRig rig = new();
		string[] lines = [.. Enumerable.Range(1, linesWritten).Select(number => $"#{number} RUN step {number}")];
		rig.Engine.Result = ServiceResult<string>.Failure(FailureMessage);
		rig.Engine.LogLines = lines;

		rig.TryStart();
		await rig.EndedAsync();

		Assert.Equal(string.Join('\n', lines[^linesKept..]), rig.Latest?.LogTail);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AFailedBuildThatWroteNoLog_HasNoLogTail()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Result = ServiceResult<string>.Failure(FailureMessage);

		rig.TryStart();
		await rig.EndedAsync();

		SandboxImageBuild? build = rig.Latest;
		Assert.NotNull(build);
		Assert.Equal(SandboxImageBuildState.Failed, build.State);
		Assert.True(File.Exists(rig.LogPath), "The engine empties the log when a build begins, so the build log exists.");
		Assert.Null(build.LogTail);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task AFailureWithoutAMessage_SaysTheImageCouldNotBeBuilt()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Result = new ServiceResult<string>(false, null, null);

		rig.TryStart();
		await rig.EndedAsync();

		Assert.Equal((SandboxImageBuildState.Failed, "The image could not be built."), (rig.Latest?.State, rig.Latest?.Error));
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AnEngineThatDoesNotAnswer_FailsTheBuildWithItsMessageAndIsNotASuccess(bool unavailableWhenAsked)
	{
		const string unavailable = "Docker is not running. Start Docker Desktop, then try again.";
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.UnavailableWhenAsked = unavailableWhenAsked ? unavailable : null;
		rig.Engine.UnavailableWhileBuilding = unavailableWhenAsked ? null : unavailable;

		rig.TryStart();
		await rig.EndedAsync();

		SandboxImageBuild? build = rig.Latest;
		Assert.NotNull(build);
		Assert.Equal((SandboxImageBuildState.Failed, unavailable, (string?)null), (build.State, build.Error, build.Tag));
		Assert.Null(build.LogTail);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Cancel_WhileTheBuildRuns_StopsItInTheEngineAndLeavesNoResultOfItsOwn()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.Engine.LogLines = ["#1 RUN not finished"];
		rig.TryStart();
		await rig.BeganAsync();
		Assert.Equal(SandboxImageBuildState.Running, rig.Latest?.State);

		bool cancelled = rig.Builds.Cancel();
		await rig.CancelledAsync();
		await rig.EndedAsync();

		Assert.True(cancelled, "There was a build to cancel.");
		Assert.Null(rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.True(rig.Builds.CanStart, "A cancelled build frees the slot.");
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Cancel_WithNothingRunning_ReturnsFalse()
	{
		await using SandboxImageBuildsRig rig = new();

		bool before = rig.Builds.Cancel();
		rig.TryStart();
		await rig.EndedAsync();
		bool after = rig.Builds.Cancel();

		Assert.False(before, "There is no build to cancel before the first.");
		Assert.False(after, "A build that ended cannot be cancelled.");
		Assert.Equal(SandboxImageBuildState.Succeeded, rig.Latest?.State);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Dispose_CancelsARunningPersonsBuild()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.TryStart();
		await rig.BeganAsync();

		rig.DisposeBuilds();
		await rig.CancelledAsync();
		await rig.EndedAsync();

		Assert.Null(rig.Latest);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildState.Succeeded)]
	[InlineData(SandboxImageBuildState.Failed)]
	[InlineData(SandboxImageBuildState.Cancelled)]
	public async Task NextEndAsync_CompletesWhenABuildEndsHoweverItEnds(SandboxImageBuildState outcome)
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		rig.Engine.Result = outcome == SandboxImageBuildState.Failed ? ServiceResult<string>.Failure(FailureMessage) : ServiceResult<string>.Success(ImageBuildingSandboxes.Tag);
		rig.TryStart();
		await rig.BeganAsync();
		Task ended = rig.Builds.NextEndAsync();
		Assert.False(ended.IsCompleted, "A build that runs has not ended.");

		if (outcome == SandboxImageBuildState.Cancelled)
		{
			Assert.True(rig.Builds.Cancel(), "The build runs, so it can be cancelled.");
		}
		else
		{
			rig.Engine.Finish();
		}

		await SandboxImageBuildsRig.WithinAsync(ended);
		await rig.EndedAsync();

		SandboxImageBuildState? result = outcome == SandboxImageBuildState.Cancelled ? null : outcome;
		Assert.Equal(result, rig.Latest?.State);
		Assert.False(rig.Builds.NextEndAsync().IsCompleted, "Only the next end completes a wait taken after this one.");
	}
}
