using System.Diagnostics;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes"/> with a real engine, around starting a sandbox: the engine is reported available, a container and its volumes carry the labels a cleanup finds them by, and a sandbox that cannot start is a <see cref="SandboxException"/> saying why, with nothing left running: one the engine refuses to make, one whose start script fails, and one whose image is not on the machine, which is refused at once without asking a registry for it.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesStartTests
{
	// An address in the range reserved for documentation, so a pull from its registry would hang rather than fail at once.
	private const string MissingImage = "192.0.2.1:5000/pyxie-test/missing:latest";

	private static readonly TimeSpan s_quickly = TimeSpan.FromSeconds(10);

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
	public async Task StartAsync_ContainerTheEngineRefuses_ThrowsSandboxExceptionSayingWhy()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Review) with { Name = "pyxie test: not a valid container name" };

		SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => rig.Sandboxes.StartAsync(spec, Token));

		Assert.StartsWith("The sandbox could not start:", exception.Message, StringComparison.Ordinal);
		Assert.Empty(await rig.Docker.RunCheckedAsync("ps", "--all", "--quiet", "--filter", $"label={SandboxNames.ProjectLabel}={rig.ProjectKey}"));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_ImageNotOnThisMachine_ThrowsSandboxExceptionAtOnceWithoutPulling()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Review) with { Image = MissingImage };
		Stopwatch watch = Stopwatch.StartNew();

		SandboxException exception = await Assert.ThrowsAsync<SandboxException>(() => rig.Sandboxes.StartAsync(spec, Token));

		Assert.Contains(MissingImage, exception.Message, StringComparison.Ordinal);
		Assert.Contains("is not on this machine", exception.Message, StringComparison.Ordinal);
		Assert.True(watch.Elapsed < s_quickly, $"Refusing the missing image took {watch.Elapsed}; the registry at that address never answers, so the engine was asked to pull it.");
		Assert.Empty(await rig.Docker.RunCheckedAsync("ps", "--all", "--quiet", "--filter", $"label={SandboxNames.ProjectLabel}={rig.ProjectKey}"));
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
