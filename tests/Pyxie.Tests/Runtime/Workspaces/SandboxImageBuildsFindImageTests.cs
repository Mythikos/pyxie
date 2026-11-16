using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Finding a project's image (<see cref="SandboxImageBuilds.FindImageAsync"/>): it asks the engine with the key of the project's folder and the Dockerfile it is given, every time, and answers with what the engine says, whatever the builds of this run of Pyxie did; an engine that does not answer is the caller's to handle. Finding is not a change to the builds, so it announces nothing.</summary>
public sealed class SandboxImageBuildsFindImageTests
{
	private const string OtherDockerfile = "ARG PYXIE_BASE\r\nFROM ${PYXIE_BASE}\r\nRUN echo other tools\r\n";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AsksTheEngineWithTheKeyOfTheProjectsFolderAndTheDockerfileAsGiven()
	{
		await using SandboxImageBuildsRig rig = new();
		string otherProject = rig.CreateOtherProject();

		await rig.Builds.FindImageAsync(otherProject, OtherDockerfile, Token);
		await rig.Builds.FindImageAsync(rig.ProjectFolder, SandboxImageBuildsRig.Dockerfile, Token);

		Assert.Equal([(SandboxNames.ProjectKey(otherProject), OtherDockerfile), (rig.ProjectKey, SandboxImageBuildsRig.Dockerfile)], rig.Engine.Lookups);
		Assert.Empty(rig.Engine.Requests);
		Assert.Empty(rig.Snapshots);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AnswersWhatTheEngineHasEachTimeItIsAsked()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.ImagePresent = true;

		string? whileThere = await rig.Builds.FindImageAsync(rig.ProjectFolder, SandboxImageBuildsRig.Dockerfile, Token);
		rig.Engine.ImagePresent = false;
		string? onceGone = await rig.Builds.FindImageAsync(rig.ProjectFolder, SandboxImageBuildsRig.Dockerfile, Token);

		Assert.Equal(ImageBuildingSandboxes.Tag, whileThere);
		Assert.Null(onceGone);
		Assert.Equal(2, rig.Engine.Lookups.Count);
		Assert.Null(rig.Latest);
		Assert.Empty(rig.Snapshots);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AfterASucceededBuildWhoseImageIsGone_AnswersNothing()
	{
		await using SandboxImageBuildsRig rig = new();
		Assert.True(rig.TryStart(), "A build starts while nothing else builds.");
		await rig.EndedAsync();
		Assert.Equal(ImageBuildingSandboxes.Tag, rig.Latest?.Tag);
		int announced = rig.Snapshots.Count;

		string? found = await rig.Builds.FindImageAsync(rig.ProjectFolder, SandboxImageBuildsRig.Dockerfile, Token);

		Assert.True(found is null, $"The engine does not have the image, but finding it answered {found} from the build that succeeded.");
		Assert.Equal(announced, rig.Snapshots.Count);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_WhenTheEngineDoesNotAnswer_ThrowsItsSandboxUnavailableException()
	{
		await using SandboxImageBuildsRig rig = new();
		rig.Engine.UnavailableWhenAsked = "Docker is not running.";

		SandboxUnavailableException exception = await Assert.ThrowsAsync<SandboxUnavailableException>(() => rig.Builds.FindImageAsync(rig.ProjectFolder, SandboxImageBuildsRig.Dockerfile, Token));

		Assert.Equal("Docker is not running.", exception.Message);
		Assert.Equal([(rig.ProjectKey, SandboxImageBuildsRig.Dockerfile)], rig.Engine.Lookups);
		Assert.Null(rig.Latest);
		Assert.Empty(rig.Snapshots);
	}
}
