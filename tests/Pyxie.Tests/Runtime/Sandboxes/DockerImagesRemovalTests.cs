using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerImages.RemoveUnusedProjectImagesAsync"/> with a real engine: a project's other images go and the one it keeps stays, an image a container uses stays until the container is gone, an image with no tag, which is what a rebuild leaves behind on some engines, still goes because of the label it carries, and nothing of another project, nor Pyxie's base image, is touched. Each test builds tiny images on Pyxie's base image and removes them through a project key of its own; it never removes unused base images or build cache, which are shared by every project on the machine.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerImagesRemovalTests
{
	private readonly DockerImageFixture _image;

	public DockerImagesRemovalTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveUnusedProjectImagesAsync_TwoImages_RemovesTheFirstAndKeepsTheSecondAndTheBase()
	{
		await using DockerSandboxRig rig = new(_image);
		string first = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("first"), Token);
		string second = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("second"), Token);

		await rig.Images.RemoveUnusedProjectImagesAsync(rig.ProjectKey, second, Token);

		Assert.False(await rig.Docker.ImageExistsAsync(first), "The project's other image was not removed.");
		Assert.True(await rig.Docker.ImageExistsAsync(second), "The image to keep was removed.");
		Assert.True(await rig.Docker.ImageExistsAsync(DockerImages.BaseTag), "Pyxie's base image is shared by every project, so removing a project's images must leave it.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveUnusedProjectImagesAsync_ImageAContainerUses_StaysUntilTheContainerIsRemoved()
	{
		await using DockerSandboxRig rig = new(_image);
		string first = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("first"), Token);
		string second = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("second"), Token);
		string container = SandboxNames.Container(rig.ProjectKey, 1, SandboxRole.Worker);
		await rig.Docker.RunCheckedAsync("create", "--name", container, "--label", $"{SandboxNames.ProjectLabel}={rig.ProjectKey}", first);

		await rig.Images.RemoveUnusedProjectImagesAsync(rig.ProjectKey, second, Token);

		Assert.True(await rig.Docker.ImageExistsAsync(first), "An image a container still uses was removed.");
		Assert.True(await rig.Docker.ImageExistsAsync(second), "The image to keep was removed.");

		await rig.Docker.RunCheckedAsync("rm", "--force", container);
		await rig.Images.RemoveUnusedProjectImagesAsync(rig.ProjectKey, second, Token);

		Assert.False(await rig.Docker.ImageExistsAsync(first), "The image was not removed by the next call, after its container was gone.");
		Assert.True(await rig.Docker.ImageExistsAsync(second), "The image to keep was removed.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveUnusedProjectImagesAsync_AnotherProjectsImage_IsLeftAlone()
	{
		await using DockerSandboxRig rig = new(_image);
		await using DockerSandboxRig otherProject = new(_image);
		string first = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("same"), Token);
		string second = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("newer"), Token);
		string other = await otherProject.BuildImageAsync(DockerSandboxRig.TinyDockerfile("same"), Token);
		Assert.True(first.Split(':')[1] == other.Split(':')[1], $"The test needs two projects whose images have the same tag text, but they are {first} and {other}.");

		await rig.Images.RemoveUnusedProjectImagesAsync(rig.ProjectKey, second, Token);

		Assert.False(await rig.Docker.ImageExistsAsync(first), "The project's other image was not removed.");
		Assert.True(await rig.Docker.ImageExistsAsync(second), "The image to keep was removed.");
		Assert.True(await rig.Docker.ImageExistsAsync(other), "Another project's image was removed, though it is built from the same Dockerfile as one that was.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveUnusedProjectImagesAsync_ImageWithoutATag_IsRemovedByItsLabel()
	{
		await using DockerSandboxRig rig = new(_image);
		string kept = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("kept"), Token);
		string untagged = await rig.Docker.BuildAsync(tag: null, $"FROM {rig.Image}\nRUN echo left > /pyxie-test-word\n", rig.ProjectKey);
		Assert.True(await rig.Docker.ImageExistsAsync(untagged), "The test needs an image without a tag, but the engine did not keep it.");

		await rig.Images.RemoveUnusedProjectImagesAsync(rig.ProjectKey, kept, Token);

		Assert.False(await rig.Docker.ImageExistsAsync(untagged), "An image without a tag, which a rebuild leaves behind on some engines, was not removed.");
		Assert.True(await rig.Docker.ImageExistsAsync(kept), "The image to keep was removed.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveUnusedProjectImagesAsync_TagMovedToANewImage_LeavesOnlyTheNewImage()
	{
		await using DockerSandboxRig rig = new(_image);
		string tag = $"{SandboxNames.ProjectImage(rig.ProjectKey)}:moved";
		await rig.Docker.BuildAsync(tag, $"FROM {rig.Image}\nRUN echo first > /pyxie-test-word\n", rig.ProjectKey);
		await rig.Docker.BuildAsync(tag, $"FROM {rig.Image}\nRUN echo second > /pyxie-test-word\n", rig.ProjectKey);
		string current = await rig.Docker.ImageIdAsync(tag);

		await rig.Images.RemoveUnusedProjectImagesAsync(rig.ProjectKey, tag, Token);
		IReadOnlySet<string> left = await rig.Docker.ProjectImageIdsAsync(rig.ProjectKey);

		Assert.True(left.SetEquals([current]), $"The project's images after the removal were [{string.Join(", ", left)}], not just the one its tag names ({current}).");
	}
}
