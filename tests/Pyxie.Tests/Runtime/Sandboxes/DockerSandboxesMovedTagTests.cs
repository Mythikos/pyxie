using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>A kept worker sandbox when a rebuild moves the image's tag to a different image, with a real engine: the spec names the same tag, but the container was made from the old image, so it is made again from the new one, on the same volumes. The worker's files survive and what the old container held in itself does not.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesMovedTagTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesMovedTagTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_KeptWorkerWhoseTagMovedToAnotherImage_IsMadeAgainOnTheSameVolumes()
	{
		await using DockerSandboxRig rig = new(_image);
		string tag = $"{SandboxNames.ProjectImage(rig.ProjectKey)}:moved";
		await rig.Docker.BuildAsync(tag, Dockerfile(rig, "first"), rig.ProjectKey);
		string firstImage = await rig.Docker.ImageIdAsync(tag);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker) with { Image = tag };
		string container;
		await using (ISandbox first = await rig.Sandboxes.StartAsync(spec, Token))
		{
			container = await rig.Docker.ContainerIdAsync(spec.Name);
			await first.WriteFileAsync("/tmp/marker", "here", Token);
			await first.WriteFileAsync("/work/kept.txt", "work", Token);
			await first.WriteFileAsync("/home/node/kept.txt", "home", Token);
		}

		await rig.Docker.BuildAsync(tag, Dockerfile(rig, "second"), rig.ProjectKey);
		string secondImage = await rig.Docker.ImageIdAsync(tag);
		Assert.True(firstImage != secondImage, "The test needs the tag to move to a different image, but building again gave the same one.");
		await using ISandbox second = await rig.Sandboxes.StartAsync(spec, Token);

		Assert.NotEqual(container, await rig.Docker.ContainerIdAsync(spec.Name));
		Assert.Null(await second.ReadFileAsync("/tmp/marker", 100, Token));
		Assert.Equal("work", await second.ReadFileAsync("/work/kept.txt", 100, Token));
		Assert.Equal("home", await second.ReadFileAsync("/home/node/kept.txt", 100, Token));
		Assert.Equal("second", (await second.RunAsync(new SandboxCommand("cat", ["/pyxie-test-word"], second.WorkPath), Token)).StandardOutput.Trim());
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_KeptWorkerWhoseTagStayedOnTheSameImage_IsPickedUpAgain()
	{
		await using DockerSandboxRig rig = new(_image);
		string tag = $"{SandboxNames.ProjectImage(rig.ProjectKey)}:moved";
		await rig.Docker.BuildAsync(tag, Dockerfile(rig, "first"), rig.ProjectKey);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker) with { Image = tag };
		string container;
		await using (ISandbox first = await rig.Sandboxes.StartAsync(spec, Token))
		{
			container = await rig.Docker.ContainerIdAsync(spec.Name);
			await first.WriteFileAsync("/tmp/marker", "here", Token);
		}

		await using ISandbox second = await rig.Sandboxes.StartAsync(spec, Token);

		Assert.Equal(container, await rig.Docker.ContainerIdAsync(spec.Name));
		Assert.Equal("here", await second.ReadFileAsync("/tmp/marker", 100, Token));
	}

	// The step writes the word the test reads back from the new container, and the label makes the image differ even in what it is built from.
	private static string Dockerfile(DockerSandboxRig rig, string word)
	{
		return $"FROM {rig.Image}\nLABEL pyxie.test.word={word}\nRUN echo {word} > /pyxie-test-word\n";
	}
}
