using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes"/> with a real engine, around starting a sandbox: the engine is reported available, a container and its volumes carry the labels a cleanup finds them by, and a sandbox that cannot start is a <see cref="SandboxException"/> saying why, with nothing left running.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesStartTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesStartTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CheckAvailableAsync_RunningLinuxEngine_Succeeds()
	{
		using ProcessRunner runner = new(TimeProvider.System);

		ServiceResult result = await new DockerSandboxes(runner, TimeProvider.System).CheckAvailableAsync(Token);

		Assert.True(result.Succeeded, result.Error);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_Worker_LabelsTheContainerAndItsVolumesForCleanup()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(3, SandboxRole.Worker);

		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);

		Assert.Equal(rig.ProjectKey, await LabelAsync(rig, "container", spec.Name, SandboxNames.ProjectLabel));
		Assert.Equal("3", await LabelAsync(rig, "container", spec.Name, SandboxNames.CardLabel));
		Assert.Equal("work", await LabelAsync(rig, "container", spec.Name, SandboxNames.RoleLabel));
		Assert.Matches("^[0-9a-f]{16}$", await LabelAsync(rig, "container", spec.Name, SandboxNames.SpecLabel));
		foreach (SandboxVolume volume in spec.Volumes)
		{
			Assert.Equal(rig.ProjectKey, await LabelAsync(rig, "volume", volume.Name, SandboxNames.ProjectLabel));
			Assert.Equal("3", await LabelAsync(rig, "volume", volume.Name, SandboxNames.CardLabel));
		}
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_ImageThatCannotBeUsed_ThrowsSandboxExceptionSayingWhy()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Review) with { Image = "Pyxie-Test-Not-A-Valid-Reference" };

		SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => rig.Sandboxes.StartAsync(spec, Token));

		Assert.StartsWith("The sandbox could not start:", exception.Message, StringComparison.Ordinal);
		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), "A sandbox that could not start left a container behind.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_StartScriptThatFails_ThrowsSandboxExceptionWithItsLogAndRemovesTheContainer()
	{
		await using DockerSandboxRig rig = new(_image);
		string dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nENTRYPOINT [\"sh\", \"-c\", \"echo pyxie-test-start-failed; exit 1\"]\n";
		ServiceResult<string> image = await rig.Sandboxes.EnsureImageAsync(new SandboxImageRequest(rig.ProjectKey, dockerfile, Rebuild: false, Path.Combine(rig.Folder, "image.log")), Token);
		Assert.True(image.Succeeded, image.Error);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Gates) with { Image = image.Value ?? string.Empty };

		SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => rig.Sandboxes.StartAsync(spec, Token));

		Assert.Contains("pyxie-test-start-failed", exception.Message, StringComparison.Ordinal);
		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), "A sandbox that stopped as it started left its container behind.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_FolderWithACommaInItsPath_IsRefused()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Review);
		spec = spec with { InputFolder = Path.Combine(rig.Folder, "in,put") };

		SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => rig.Sandboxes.StartAsync(spec, Token));

		Assert.Contains("comma", exception.Message, StringComparison.Ordinal);
		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), "A refused sandbox left a container behind.");
	}

	private static Task<string> LabelAsync(DockerSandboxRig rig, string kind, string name, string label)
	{
		return rig.Docker.RunCheckedAsync(kind, "inspect", "--format", $"{{{{index .{(kind == "volume" ? "Labels" : "Config.Labels")} \"{label}\"}}}}", name);
	}
}
