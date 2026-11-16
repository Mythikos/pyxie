using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="SandboxImageRequest.BuildStarting"/> with a real engine: it is called once when the image has to be built, and never when the image is already there, which is how a request that only finds its image is never reported as a build.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesBuildStartingTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesBuildStartingTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_ImageToBuild_CallsBuildStartingOnce()
	{
		await using DockerSandboxRig rig = new(_image);
		int starts = 0;
		SandboxImageRequest request = new(rig.ProjectKey, DockerSandboxRig.TinyDockerfile("starting"), Rebuild: false, Path.Combine(rig.Folder, "image.log")) { BuildStarting = () => Interlocked.Increment(ref starts) };

		ServiceResult<string> result = await rig.Sandboxes.EnsureImageAsync(request, Token);

		Assert.True(result.Succeeded, result.Error);
		Assert.Equal(1, Volatile.Read(ref starts));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_ImageAlreadyThere_NeverCallsBuildStarting()
	{
		await using DockerSandboxRig rig = new(_image);
		string dockerfile = DockerSandboxRig.TinyDockerfile("there");
		await rig.BuildImageAsync(dockerfile, Token);
		int starts = 0;
		SandboxImageRequest request = new(rig.ProjectKey, dockerfile, Rebuild: false, Path.Combine(rig.Folder, "image.log")) { BuildStarting = () => Interlocked.Increment(ref starts) };

		ServiceResult<string> result = await rig.Sandboxes.EnsureImageAsync(request, Token);

		Assert.True(result.Succeeded, result.Error);
		Assert.Equal(0, Volatile.Read(ref starts));
	}
}
