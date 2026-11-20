using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Forgetting a project that is being removed (<see cref="SandboxImageBuilds.ForgetProjectAsync"/>): a person's build of its image is cancelled and has ended when the call returns, and its last build is dropped and that is announced, so a project that later gets the same key starts without it. Another project's build keeps running, and its last build stays.</summary>
public sealed class SandboxImageBuildsForgetTests
{
	[Fact]
	public async Task ForgetProjectAsync_WhileAPersonsBuildOfTheProjectRuns_CancelsItAndReturnsOnceItEnded()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.Holding = true;
		Assert.True(rig.TryStart(), "A person's build can be asked for while none builds.");
		await rig.QueuedAsync();
		await rig.BeganAsync();

		await SandboxImageBuildsRig.WithinAsync(rig.Builds.ForgetProjectAsync(rig.ProjectKey));

		Assert.True(rig.Builds.Running.IsCompleted, "The build should have ended by the time the project is forgotten.");
		Assert.Null(rig.Builds.PersonBuild);
		Assert.Null(rig.Latest);
		Assert.True(rig.Builds.CanStart, "Nothing builds any more.");
	}

	[Fact]
	public async Task ForgetProjectAsync_WhileAnotherProjectsBuildRuns_LeavesItRunning()
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		rig.Engine.Holding = true;
		Assert.True(rig.TryStart(other), "A person's build can be asked for while none builds.");
		await rig.QueuedAsync();
		await rig.BeganAsync();

		await SandboxImageBuildsRig.WithinAsync(rig.Builds.ForgetProjectAsync(rig.ProjectKey));

		Assert.False(rig.Builds.Running.IsCompleted, "Another project's build must keep running.");
		Assert.Equal(SandboxNames.ProjectKey(other), rig.Builds.PersonBuild?.ProjectKey);
		rig.Engine.Finish();
		await SandboxImageBuildsRig.WithinAsync(rig.Builds.Running);
		Assert.Equal(SandboxImageBuildState.Succeeded, rig.LatestOf(other)?.State);
	}

	[Fact]
	public async Task ForgetProjectAsync_AfterTheProjectBuilt_DropsItsLastBuildAnnouncesThatAndKeepsAnotherProjects()
	{
		await using SandboxImageBuildsRig rig = new();
		string other = rig.CreateOtherProject();
		Assert.True(rig.TryStart(), "A person's build can be asked for while none builds.");
		await SandboxImageBuildsRig.WithinAsync(rig.Builds.Running);
		Assert.True(rig.TryStart(other), "A person's build can be asked for while none builds.");
		await SandboxImageBuildsRig.WithinAsync(rig.Builds.Running);
		Assert.NotNull(rig.Latest);
		int changes = 0;
		rig.Builds.Changed += () => changes++;

		await SandboxImageBuildsRig.WithinAsync(rig.Builds.ForgetProjectAsync(rig.ProjectKey));

		Assert.Null(rig.Latest);
		Assert.Equal(SandboxImageBuildState.Succeeded, rig.LatestOf(other)?.State);
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task ForgetProjectAsync_OfAProjectThatNeverBuilt_AnnouncesNothing()
	{
		await using SandboxImageBuildsRig rig = new();
		int changes = 0;
		rig.Builds.Changed += () => changes++;

		await SandboxImageBuildsRig.WithinAsync(rig.Builds.ForgetProjectAsync(rig.ProjectKey));

		Assert.Equal(0, changes);
		Assert.Null(rig.Latest);
	}
}
