using System.Text.Json;

using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>How Pyxie's images are marked in a real engine, which is how a later cleanup tells them from everything else on the machine: a project's image is labelled as its own project's image whatever the base image it is built on says, the base image is labelled as the base, and both run every build step with Pyxie's build shell, which is how the build cache of Pyxie's images is found (see <see cref="DockerBuildCacheTests"/>).</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesImageLabelsTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesImageLabelsTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_ProjectImage_IsLabelledAsTheProjectsOwnAndNotAsABaseImage()
	{
		await using DockerSandboxRig rig = new(_image);

		string tag = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("labels"), Token);

		Assert.Equal(SandboxNames.ProjectImageKind, await rig.Docker.ImageLabelAsync(tag, SandboxNames.ImageLabel));
		Assert.Equal(rig.ProjectKey, await rig.Docker.ImageLabelAsync(tag, SandboxNames.ProjectLabel));
		Assert.Equal(SandboxNames.BaseImageKind, await rig.Docker.ImageLabelAsync(DockerImages.BaseTag, SandboxNames.ImageLabel));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_ProjectImage_RunsItsStepsWithTheBuildShell()
	{
		await using DockerSandboxRig rig = new(_image);

		string tag = await rig.BuildImageAsync(DockerSandboxRig.TinyDockerfile("shell"), Token);

		Assert.Equal([SandboxNames.BuildShell, "-c"], await ShellAsync(rig, tag));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task BaseImage_BuiltForTheFixture_IsLabelledAsTheBaseAndRunsItsStepsWithTheBuildShell()
	{
		await using DockerSandboxRig rig = new(_image);

		Assert.Equal(SandboxNames.BaseImageKind, await rig.Docker.ImageLabelAsync(DockerImages.BaseTag, SandboxNames.ImageLabel));
		Assert.Equal(string.Empty, await rig.Docker.ImageLabelAsync(DockerImages.BaseTag, SandboxNames.ProjectLabel));
		Assert.Equal([SandboxNames.BuildShell, "-c"], await ShellAsync(rig, DockerImages.BaseTag));
	}

	private static async Task<string[]> ShellAsync(DockerSandboxRig rig, string tag)
	{
		string json = await rig.Docker.RunCheckedAsync("image", "inspect", "--format", "{{json .Config.Shell}}", tag);

		return JsonSerializer.Deserialize<string[]>(json) ?? [];
	}
}
