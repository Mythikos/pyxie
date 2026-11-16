using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A person's build and a round's wait for the same image. Builds run one at a time in the order they asked, and a request is only reported as a build once it begins: a person who asks while a round waits for its turn gets a build that waits behind the round's, and <see cref="SandboxImageBuilds.Building"/> follows whichever build is running, and shows a person's build that only waits when none runs. The engine stand-in holds the engine as a build ahead of both, then lets the round, which asked first, take its turn.</summary>
public sealed class SandboxImageBuildsOrderTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	// A round waits for its turn behind a build ahead, and a person asks while it waits; the engine is still held.
	private static async Task<Task<ServiceResult<string>>> ARoundThenAPersonAreWaitingAsync(SandboxImageBuildsRig rig)
	{
		rig.Engine.Holding = true;
		await rig.Engine.OccupyAsync(Token);
		Task<ServiceResult<string>> round = rig.EnsureAsync(Token);
		Assert.False((await rig.QueuedAsync()).Rebuild, "The round asks first, and never for a rebuild.");

		Assert.True(rig.TryStart(), "A build that has not begun is not running, so a person can ask while the round waits for its turn.");
		Assert.True((await rig.QueuedAsync()).Rebuild, "The person asks second, for a rebuild.");

		return round;
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task APersonWhoAsksWhileARoundWaits_ShowsTheirBuildAsBuildingUntilTheRoundBegins()
	{
		await using SandboxImageBuildsRig rig = new();

		Task<ServiceResult<string>> round = await ARoundThenAPersonAreWaitingAsync(rig);

		SandboxImageBuild? person = rig.Builds.PersonBuild;
		Assert.NotNull(person);
		Assert.Equal((SandboxImageBuildRequester.Person, SandboxImageBuildState.Running), (person.RequestedBy, person.State));
		Assert.Equal(person, rig.Builds.Building);
		Assert.Equal(person, rig.Latest);
		Assert.False(rig.Builds.CanStart, "The person's build waits, so nothing else can be asked for.");

		// Leaves the engine free and both finished, so nothing is left running.
		rig.Engine.Vacate();
		await rig.BeganAsync();
		rig.Engine.Finish();
		await SandboxImageBuildsRig.WithinAsync(round);
		await rig.BeganAsync();
		rig.Engine.Finish();
		await rig.EndedAsync();
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WhenTheRoundWinsTheEngine_ItsBuildIsTheOneBuildingWhileThePersonsBuildStaysTheirs()
	{
		await using SandboxImageBuildsRig rig = new();
		Task<ServiceResult<string>> round = await ARoundThenAPersonAreWaitingAsync(rig);
		SandboxImageBuild? person = rig.Builds.PersonBuild;

		rig.Engine.Vacate();
		SandboxImageRequest began = await rig.BeganAsync();

		Assert.False(began.Rebuild, "The round asked first, so its build begins first.");
		SandboxImageBuild? building = rig.Builds.Building;
		Assert.NotNull(building);
		Assert.Equal((SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Running), (building.RequestedBy, building.State));
		Assert.Same(building, rig.Latest);
		Assert.Same(person, rig.Builds.PersonBuild);
		Assert.False(rig.Builds.CanStart, "A build runs and another waits.");

		rig.Engine.Finish();
		await SandboxImageBuildsRig.WithinAsync(round);
		await rig.BeganAsync();
		rig.Engine.Finish();
		await rig.EndedAsync();
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CancellingThePersonsBuildWhileTheRoundBuilds_LeavesTheRoundsBuildAsBuilding()
	{
		await using SandboxImageBuildsRig rig = new();
		Task<ServiceResult<string>> round = await ARoundThenAPersonAreWaitingAsync(rig);
		rig.Engine.Vacate();
		await rig.BeganAsync();
		SandboxImageBuild? roundsBuild = rig.Builds.Building;

		bool cancelled = rig.Builds.Cancel();
		await rig.CancelledAsync();
		await rig.EndedAsync();

		Assert.True(cancelled, "The person's build waits, and can be cancelled.");
		Assert.Same(roundsBuild, rig.Builds.Building);
		Assert.Same(roundsBuild, rig.Latest);
		Assert.Equal(SandboxImageBuildRequester.Pipeline, roundsBuild?.RequestedBy);
		Assert.Equal(SandboxImageBuildState.Running, roundsBuild?.State);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.False(rig.Builds.CanStart, "The round's build still runs.");
		Assert.Equal(0, rig.Builds.SucceededBuilds);

		rig.Engine.Finish();
		ResultAssert.Succeeded(await SandboxImageBuildsRig.WithinAsync(round));
		Assert.Equal((SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Succeeded), (rig.Latest?.RequestedBy, rig.Latest?.State));
		Assert.Null(rig.Builds.Building);
		Assert.True(rig.Builds.CanStart, "Nothing builds any more.");
		Assert.Equal(1, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WithoutCancelling_ThePersonsBuildRunsAfterTheRoundsAndBecomesTheOneBuilding()
	{
		await using SandboxImageBuildsRig rig = new();
		Task<ServiceResult<string>> round = await ARoundThenAPersonAreWaitingAsync(rig);
		SandboxImageBuild? person = rig.Builds.PersonBuild;
		rig.Engine.Vacate();
		await rig.BeganAsync();

		rig.Engine.Finish();
		ResultAssert.Succeeded(await SandboxImageBuildsRig.WithinAsync(round));
		SandboxImageRequest began = await rig.BeganAsync();

		Assert.True(began.Rebuild, "The person's build begins once the round's ended.");
		Assert.Equal(person, rig.Builds.Building);
		Assert.Equal(person, rig.Latest);
		Assert.Equal(person, rig.Builds.PersonBuild);
		Assert.Equal(1, rig.Builds.SucceededBuilds);

		rig.Engine.Finish();
		await rig.EndedAsync();

		Assert.Equal(person?.Id, rig.Latest?.Id);
		Assert.Equal(SandboxImageBuildState.Succeeded, rig.Latest?.State);
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.Equal(2, rig.Builds.SucceededBuilds);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CancellingAPersonsBuildThatWaitsForABuildNobodyReported_EndsItWithNoResult()
	{
		await using SandboxImageBuildsRig rig = new();
		await rig.Engine.OccupyAsync(Token);
		rig.TryStart();
		await rig.QueuedAsync();

		bool cancelled = rig.Builds.Cancel();
		await rig.CancelledAsync();
		await rig.EndedAsync();
		rig.Engine.Vacate();

		Assert.True(cancelled, "The person's build waits, and can be cancelled.");
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Latest);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.True(rig.Builds.CanStart, "Nothing builds any more.");
		Assert.Equal(0, rig.Builds.SucceededBuilds);
	}
}
