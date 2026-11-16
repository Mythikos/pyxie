using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes.FindImageAsync"/> with a real engine: once a project's image for a Dockerfile is built it answers with that image's tag, from any instance and whatever the Dockerfile's line endings; it answers nothing for another Dockerfile or another project while that image is there, and nothing once the image is removed, because it asks the engine every time. Finding never builds. Each test builds a tiny image on the base image <see cref="DockerImageFixture"/> made, so nothing is pulled.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesFindImageTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesFindImageTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AfterTheImageWasBuilt_ReturnsItsTagFromAnyInstanceWhateverTheLineEndings()
	{
		await using DockerSandboxRig rig = new(_image);
		string dockerfile = DockerSandboxRig.TinyDockerfile("find-built");
		string built = await rig.BuildImageAsync(dockerfile, Token);

		string? found = await rig.Sandboxes.FindImageAsync(rig.ProjectKey, dockerfile, Token);
		string? foundWithCrLf = await rig.Sandboxes.FindImageAsync(rig.ProjectKey, dockerfile.ReplaceLineEndings("\r\n"), Token);

		// A new instance knows nothing of what the first one built, so only the engine can tell it the image is there.
		using ProcessRunner runner = new(TimeProvider.System);
		string? foundByAnother = await new DockerSandboxes(runner, TimeProvider.System).FindImageAsync(rig.ProjectKey, dockerfile, Token);

		Assert.Equal(DockerImages.ProjectTag(rig.ProjectKey, dockerfile), found);
		Assert.Equal(built, found);
		Assert.Equal(found, foundWithCrLf);
		Assert.Equal(found, foundByAnother);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AnotherDockerfileWhileTheProjectHasAnImage_ReturnsNullAndBuildsNothing()
	{
		await using DockerSandboxRig rig = new(_image);
		string built = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("find-built"), Token);
		string builtId = await rig.Docker.ImageIdAsync(built);
		string other = DockerSandboxRig.TinyDockerfile("find-other");

		string? found = await rig.Sandboxes.FindImageAsync(rig.ProjectKey, other, Token);

		Assert.True(found is null, $"The project only has the image for another Dockerfile, but finding answered {found}.");
		Assert.False(await rig.Docker.ImageExistsAsync(DockerImages.ProjectTag(rig.ProjectKey, other)), "Finding the image for a Dockerfile built it.");
		IReadOnlySet<string> images = await rig.Docker.ProjectImageIdsAsync(rig.ProjectKey);
		Assert.True(images.SetEquals([builtId]), $"The project's images after finding were [{string.Join(", ", images)}], not just the one it had ({builtId}).");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AnotherProjectWithTheSameDockerfile_ReturnsNull()
	{
		await using DockerSandboxRig rig = new(_image);
		string dockerfile = DockerSandboxRig.TinyDockerfile("find-built");
		await rig.BuildImageAsync(dockerfile, Token);
		string otherProject = SandboxNames.ProjectKey(Path.Combine(rig.Folder, "other-project"));

		string? found = await rig.Sandboxes.FindImageAsync(otherProject, dockerfile, Token);

		Assert.True(found is null, $"Only another project has the image for the Dockerfile, but finding answered {found}.");
		Assert.False(await rig.Docker.ImageExistsAsync(DockerImages.ProjectTag(otherProject, dockerfile)), "Finding another project's image for a Dockerfile built it.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task FindImageAsync_AfterTheImageWasRemoved_ReturnsNull()
	{
		await using DockerSandboxRig rig = new(_image);
		string dockerfile = DockerSandboxRig.TinyDockerfile("find-removed");
		string built = await rig.BuildImageAsync(dockerfile, Token);
		Assert.Equal(built, await rig.Sandboxes.FindImageAsync(rig.ProjectKey, dockerfile, Token));

		await rig.Docker.RunCheckedAsync("image", "rm", built);
		string? found = await rig.Sandboxes.FindImageAsync(rig.ProjectKey, dockerfile, Token);

		Assert.True(found is null, $"The image was removed from the engine, but finding still answered {found}.");
	}
}
