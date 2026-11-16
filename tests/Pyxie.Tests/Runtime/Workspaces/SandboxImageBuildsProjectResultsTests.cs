using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>What <see cref="SandboxImageBuilds.Latest"/> keeps for each project while others build: a project's last build that succeeded or failed stays its own until it builds again, whoever asked for the builds of the others, a cancelled build leaves it as it was, and a build that runs replaces it only while it runs and by its own outcome. The engine stand-in builds the images of two projects, whose folders the rig makes, with the outcome the test scripts for the next build.</summary>
public sealed class SandboxImageBuildsProjectResultsTests
{
	private const string FailureMessage = "The project's sandbox image could not be built.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	// Scripts how the next builds end: the outcome the engine returns once a build runs, and the log it writes when one begins.
	private static void Script(SandboxImageBuildsRig rig, SandboxImageBuildState outcome, params string[] logLines)
	{
		rig.Engine.Result = outcome == SandboxImageBuildState.Failed ? ServiceResult<string>.Failure(FailureMessage) : ServiceResult<string>.Success(ImageBuildingSandboxes.Tag);
		rig.Engine.LogLines = logLines;
	}

	// Asks for a build of a project's image as its requester does: a person's, or a round's that finds the image is not there. The task completes when the build ended, and for a round it throws when the build was cancelled.
	private static Task AskAsync(SandboxImageBuildsRig rig, string projectFolder, SandboxImageBuildRequester requester, CancellationToken cancellationToken)
	{
		if (requester == SandboxImageBuildRequester.Person)
		{
			Assert.True(rig.TryStart(projectFolder), "A person's build can be asked for while none builds.");

			return rig.Builds.Running;
		}

		return rig.EnsureAsync(projectFolder, cancellationToken);
	}

	// Waits for the next build to begin and checks that it is the project's, so an announcement left by an earlier build fails here and not later.
	private static async Task BeganAsync(SandboxImageBuildsRig rig, string projectFolder)
	{
		SandboxImageRequest request = await rig.BeganAsync();
		Assert.Equal(SandboxNames.ProjectKey(projectFolder), request.ProjectKey);
	}

	// Builds a project's image to the end, and takes what the build announced off the engine, so the next wait sees the next build's.
	private static async Task<SandboxImageBuild> BuildAsync(SandboxImageBuildsRig rig, string projectFolder, SandboxImageBuildRequester requester)
	{
		await SandboxImageBuildsRig.WithinAsync(AskAsync(rig, projectFolder, requester, Token));
		await rig.QueuedAsync();
		await BeganAsync(rig, projectFolder);

		return rig.LatestOf(projectFolder) ?? throw new InvalidOperationException("A build that ended left no result.");
	}

	// Holds a build of a project's image while it runs and cancels it the way its requester does: a person's Cancel, or the token of the round. It returns once the build ended.
	private static async Task CancelBuildAsync(SandboxImageBuildsRig rig, string projectFolder, SandboxImageBuildRequester requester)
	{
		rig.Engine.Holding = true;
		using CancellationTokenSource stopping = new();
		Task ended = AskAsync(rig, projectFolder, requester, stopping.Token);
		await BeganAsync(rig, projectFolder);
		Assert.Equal(SandboxImageBuildState.Running, rig.LatestOf(projectFolder)?.State);

		if (requester == SandboxImageBuildRequester.Person)
		{
			Assert.True(rig.Builds.Cancel(), "The build runs, so it can be cancelled.");
			await SandboxImageBuildsRig.WithinAsync(ended);
		}
		else
		{
			await stopping.CancelAsync();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SandboxImageBuildsRig.WithinAsync(ended));
		}

		rig.Engine.Holding = false;
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildRequester.Person)]
	[InlineData(SandboxImageBuildRequester.Pipeline)]
	public async Task ASucceededResult_SurvivesAnotherProjectsFailedBuild(SandboxImageBuildRequester otherRequester)
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Script(rig, SandboxImageBuildState.Succeeded);
		SandboxImageBuild succeeded = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		Script(rig, SandboxImageBuildState.Failed, "#1 RUN other fails");

		SandboxImageBuild failed = await BuildAsync(rig, other, otherRequester);

		Assert.Equal((SandboxImageBuildState.Succeeded, ImageBuildingSandboxes.Tag), (succeeded.State, succeeded.Tag));
		Assert.Equal(succeeded, rig.Latest);
		Assert.NotEqual(succeeded.Id, failed.Id);
		Assert.Equal((SandboxNames.ProjectKey(other), otherRequester, SandboxImageBuildState.Failed, FailureMessage, "#1 RUN other fails"), (failed.ProjectKey, failed.RequestedBy, failed.State, failed.Error, failed.LogTail));
		Assert.Equal(failed, rig.LatestOf(other));
		Assert.Equal(1, rig.Builds.SucceededBuilds);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildRequester.Person)]
	[InlineData(SandboxImageBuildRequester.Pipeline)]
	public async Task AFailedResult_SurvivesAnotherProjectsSucceededBuildWithItsErrorAndLogTail(SandboxImageBuildRequester otherRequester)
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Script(rig, SandboxImageBuildState.Failed, "#1 RUN this fails", "#2 error: this");
		SandboxImageBuild failed = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		Script(rig, SandboxImageBuildState.Succeeded, "#1 RUN other works");

		SandboxImageBuild succeeded = await BuildAsync(rig, other, otherRequester);

		Assert.Equal((SandboxImageBuildState.Failed, FailureMessage, "#1 RUN this fails\n#2 error: this"), (failed.State, failed.Error, failed.LogTail));
		Assert.Equal(failed, rig.Latest);
		Assert.NotEqual(failed.Id, succeeded.Id);
		Assert.Equal((SandboxNames.ProjectKey(other), otherRequester, SandboxImageBuildState.Succeeded, ImageBuildingSandboxes.Tag, (string?)null, (string?)null), (succeeded.ProjectKey, succeeded.RequestedBy, succeeded.State, succeeded.Tag, succeeded.Error, succeeded.LogTail));
		Assert.Equal(succeeded, rig.LatestOf(other));
		Assert.Equal(1, rig.Builds.SucceededBuilds);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildState.Succeeded)]
	[InlineData(SandboxImageBuildState.Failed)]
	public async Task Latest_ForAProjectThatWasNeverBuilt_IsNullWhateverOtherProjectsBuilt(SandboxImageBuildState otherOutcome)
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Script(rig, otherOutcome, "#1 RUN other");

		SandboxImageBuild built = await BuildAsync(rig, other, SandboxImageBuildRequester.Person);

		Assert.Null(rig.Latest);
		Assert.Equal(otherOutcome, built.State);
		Assert.Equal(built, rig.LatestOf(other));
		Assert.Null(rig.Builds.Building);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildRequester.Person, SandboxImageBuildState.Succeeded)]
	[InlineData(SandboxImageBuildRequester.Person, SandboxImageBuildState.Failed)]
	[InlineData(SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Succeeded)]
	[InlineData(SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Failed)]
	public async Task ACancelledBuild_LeavesTheProjectsPreviousResultAsItsLatest(SandboxImageBuildRequester cancelled, SandboxImageBuildState previousOutcome)
	{
		await using SandboxImageBuildsRig rig = new();
		Script(rig, previousOutcome, "#1 RUN previous");
		SandboxImageBuild previous = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		Assert.Equal(previousOutcome, previous.State);
		rig.Engine.LogLines = ["#1 RUN not finished"];

		await CancelBuildAsync(rig, rig.ProjectFolder, cancelled);

		Assert.Equal(previous, rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.True(rig.Builds.CanStart, "A cancelled build frees the slot.");
		Assert.Equal(previousOutcome == SandboxImageBuildState.Succeeded ? 1 : 0, rig.Builds.SucceededBuilds);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildRequester.Person)]
	[InlineData(SandboxImageBuildRequester.Pipeline)]
	public async Task ACancelledBuildWithoutAPreviousResult_LeavesNoLatestAndAnotherProjectsResultStaysItsOwn(SandboxImageBuildRequester cancelled)
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Script(rig, SandboxImageBuildState.Succeeded);
		SandboxImageBuild othersResult = await BuildAsync(rig, other, SandboxImageBuildRequester.Person);

		await CancelBuildAsync(rig, rig.ProjectFolder, cancelled);

		Assert.Null(rig.Latest);
		Assert.Equal(othersResult, rig.LatestOf(other));
		Assert.Null(rig.Builds.Building);
		Assert.Null(rig.Builds.PersonBuild);
		Assert.True(rig.Builds.CanStart, "A cancelled build frees the slot.");
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildRequester.Person)]
	[InlineData(SandboxImageBuildRequester.Pipeline)]
	public async Task WhileAProjectsRebuildRuns_ItsLatestIsTheRunningBuildAndItsOutcomeReplacesThePreviousOneAfterwards(SandboxImageBuildRequester rebuild)
	{
		await using SandboxImageBuildsRig rig = new();
		Script(rig, SandboxImageBuildState.Succeeded);
		SandboxImageBuild previous = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		Script(rig, SandboxImageBuildState.Failed, "#1 RUN rebuild fails");
		rig.Engine.Holding = true;
		rig.Clock.Tick();

		Task ended = AskAsync(rig, rig.ProjectFolder, rebuild, Token);
		await BeganAsync(rig, rig.ProjectFolder);

		SandboxImageBuild? running = rig.Latest;
		Assert.NotNull(running);
		Assert.Equal((rebuild, SandboxImageBuildState.Running), (running.RequestedBy, running.State));
		Assert.NotEqual(previous.Id, running.Id);
		Assert.Equal(running, rig.Builds.Building);

		DateTime endedUtc = rig.Clock.Tick();
		rig.Engine.Finish();
		await SandboxImageBuildsRig.WithinAsync(ended);

		Assert.Equal(running with { State = SandboxImageBuildState.Failed, EndedUtc = endedUtc, Error = FailureMessage, LogTail = "#1 RUN rebuild fails" }, rig.Latest);
		Assert.Null(rig.Builds.Building);
		Assert.Equal(1, rig.Builds.SucceededBuilds);
	}

	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxImageBuildRequester.Person)]
	[InlineData(SandboxImageBuildRequester.Pipeline)]
	public async Task Building_IsAnotherProjectsRunningBuildWhileLatestKeepsThisProjectsResult(SandboxImageBuildRequester otherRequester)
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Script(rig, SandboxImageBuildState.Failed, "#1 RUN this fails");
		SandboxImageBuild previous = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		Script(rig, SandboxImageBuildState.Succeeded);
		rig.Engine.Holding = true;

		Task ended = AskAsync(rig, other, otherRequester, Token);
		await BeganAsync(rig, other);

		SandboxImageBuild? building = rig.Builds.Building;
		Assert.NotNull(building);
		Assert.Equal((SandboxNames.ProjectKey(other), otherRequester, SandboxImageBuildState.Running), (building.ProjectKey, building.RequestedBy, building.State));
		Assert.Equal(building, rig.LatestOf(other));
		Assert.Equal(previous, rig.Latest);
		Assert.False(rig.Builds.CanStart, "Another project's build runs, and builds run one at a time.");

		rig.Engine.Finish();
		await SandboxImageBuildsRig.WithinAsync(ended);

		Assert.Null(rig.Builds.Building);
		Assert.Equal(previous, rig.Latest);
		Assert.Equal((SandboxImageBuildState.Succeeded, ImageBuildingSandboxes.Tag), (rig.LatestOf(other)?.State, rig.LatestOf(other)?.Tag));
		Assert.True(rig.Builds.CanStart, "Nothing builds any more.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Latest_WhileAPersonsBuildWaitsBehindAnotherProjectsRound_IsTheWaitingBuildAndBuildingIsTheRoundsBuild()
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Script(rig, SandboxImageBuildState.Succeeded);
		SandboxImageBuild previous = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		rig.Engine.Holding = true;
		await rig.Engine.OccupyAsync(Token);
		Task<ServiceResult<string>> round = rig.EnsureAsync(other, Token);
		Assert.False((await rig.QueuedAsync()).Rebuild, "The round asks first, and never for a rebuild.");
		Assert.True(rig.TryStart(), "A build that has not begun is not running, so a person can ask while the round waits for its turn.");
		Assert.True((await rig.QueuedAsync()).Rebuild, "The person asks second, for a rebuild.");
		SandboxImageBuild? waiting = rig.Builds.PersonBuild;
		Assert.NotNull(waiting);
		Assert.Equal(waiting, rig.Latest);

		rig.Engine.Vacate();
		await BeganAsync(rig, other);

		SandboxImageBuild? building = rig.Builds.Building;
		Assert.NotNull(building);
		Assert.Equal((SandboxNames.ProjectKey(other), SandboxImageBuildRequester.Pipeline), (building.ProjectKey, building.RequestedBy));
		Assert.Equal(building, rig.LatestOf(other));
		Assert.Equal(waiting, rig.Latest);
		Assert.NotEqual(previous, rig.Latest);

		rig.Engine.Finish();
		ResultAssert.Succeeded(await SandboxImageBuildsRig.WithinAsync(round));
		await BeganAsync(rig, rig.ProjectFolder);
		rig.Engine.Finish();
		await rig.EndedAsync();

		Assert.Equal((waiting.Id, SandboxImageBuildState.Succeeded), (rig.Latest?.Id, rig.Latest?.State));
		Assert.Equal((building.Id, SandboxImageBuildState.Succeeded), (rig.LatestOf(other)?.Id, rig.LatestOf(other)?.State));
		Assert.Null(rig.Builds.Building);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ASecondSuccessOfAProject_ReplacesItsFirst()
	{
		await using SandboxImageBuildsRig rig = new();
		Script(rig, SandboxImageBuildState.Succeeded);
		SandboxImageBuild first = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Person);
		DateTime secondEnded = rig.Clock.Tick();

		SandboxImageBuild second = await BuildAsync(rig, rig.ProjectFolder, SandboxImageBuildRequester.Pipeline);

		Assert.NotEqual(first.Id, second.Id);
		Assert.Equal((SandboxImageBuildRequester.Pipeline, SandboxImageBuildState.Succeeded, secondEnded), (second.RequestedBy, second.State, second.EndedUtc));
		Assert.NotEqual(first.EndedUtc, second.EndedUtc);
		Assert.Equal(second, rig.Latest);
		Assert.Equal(2, rig.Builds.SucceededBuilds);
	}
}
