using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>How long a Docker sandbox lives: a command that times out or is cancelled spoils it and disposing removes it, while a worker's volumes survive; a kept worker is picked up again, with what it left running ended, only while its spec is unchanged and it still runs; gate and review sandboxes go when disposed.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxLifetimeTests
{
	private static readonly TimeSpan s_shortTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan s_cancelAfter = TimeSpan.FromSeconds(1);

	private readonly DockerImageFixture _image;

	public DockerSandboxLifetimeTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_TimedOut_SpoilsTheSandboxAndRemovingItKeepsTheVolumes()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);
		await sandbox.WriteFileAsync("/work/kept.txt", "work", Token);
		await sandbox.WriteFileAsync("/home/node/kept.txt", "home", Token);

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sleep", ["600"], sandbox.WorkPath) { Timeout = s_shortTimeout }, Token);
		await sandbox.DisposeAsync();

		Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
		Assert.True(sandbox.IsSpoiled, "What a timed-out command left running inside cannot be trusted to stop, so the sandbox is spoiled.");
		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), "A spoiled sandbox must be removed when it is disposed, kept or not.");
		foreach (SandboxVolume volume in spec.Volumes)
		{
			Assert.True(await rig.Docker.VolumeExistsAsync(volume.Name), $"The worker's volume {volume.Name} went with its container.");
		}

		await using ISandbox again = await rig.Sandboxes.StartAsync(spec, Token);
		Assert.Equal("work", await again.ReadFileAsync("/work/kept.txt", 100, Token));
		Assert.Equal("home", await again.ReadFileAsync("/home/node/kept.txt", 100, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_Cancelled_ThrowsAndTheSandboxIsRemovedButNotItsVolumes()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		stop.CancelAfter(s_cancelAfter);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sandbox.RunAsync(new SandboxCommand("sleep", ["600"], sandbox.WorkPath), stop.Token));
		await sandbox.DisposeAsync();

		Assert.True(sandbox.IsSpoiled, "A stopped command may still run inside, so the sandbox is spoiled.");
		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), "A spoiled sandbox must be removed when it is disposed.");
		Assert.True(await rig.Docker.VolumeExistsAsync(spec.Volumes[0].Name), "The worker's work volume went with its container.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_KeptSandboxWithTheSameSpec_IsPickedUpAgainWithWhatItLeftRunningEnded()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		string container;
		await using (ISandbox first = await rig.Sandboxes.StartAsync(spec, Token))
		{
			container = await rig.Docker.ContainerIdAsync(spec.Name);
			ProcessResult left = await first.RunAsync(new SandboxCommand("sh", ["-c", "printf here > /tmp/marker && nohup sleep 600 >/dev/null 2>&1 &"], first.WorkPath), Token);
			Assert.True(left.Succeeded, left.Output);
		}

		Assert.True(await rig.Docker.ContainerExistsAsync(spec.Name), "A worker's sandbox must outlive its round.");

		await using ISandbox second = await rig.Sandboxes.StartAsync(spec, Token);

		Assert.Equal(container, await rig.Docker.ContainerIdAsync(spec.Name));
		Assert.Equal("here", await second.ReadFileAsync("/tmp/marker", 100, Token));
		Assert.False(await SandboxProcessProbe.IsRunningAsync(second, "sleep"), "What the last round left running must be ended before the next round starts.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_KeptSandboxWithAChangedSpec_IsMadeAgainOnTheSameVolumes()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		string container;
		await using (ISandbox first = await rig.Sandboxes.StartAsync(spec, Token))
		{
			container = await rig.Docker.ContainerIdAsync(spec.Name);
			await first.WriteFileAsync("/tmp/marker", "here", Token);
			await first.WriteFileAsync("/work/kept.txt", "work", Token);
		}

		SandboxSpec changed = spec with { Environment = new Dictionary<string, string> { ["PYXIE_TEST_SETTING"] = "changed" } };
		await using ISandbox second = await rig.Sandboxes.StartAsync(changed, Token);

		Assert.NotEqual(container, await rig.Docker.ContainerIdAsync(spec.Name));
		Assert.Null(await second.ReadFileAsync("/tmp/marker", 100, Token));
		Assert.Equal("work", await second.ReadFileAsync("/work/kept.txt", 100, Token));
		Assert.Equal("changed", (await second.RunAsync(new SandboxCommand("printenv", ["PYXIE_TEST_SETTING"], second.WorkPath), Token)).StandardOutput.Trim());
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_KeptSandboxThatStopped_IsMadeAgain()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		string container;
		await using (ISandbox first = await rig.Sandboxes.StartAsync(spec, Token))
		{
			container = await rig.Docker.ContainerIdAsync(spec.Name);
		}

		await rig.Docker.RunCheckedAsync("kill", spec.Name);
		await using ISandbox second = await rig.Sandboxes.StartAsync(spec, Token);

		Assert.NotEqual(container, await rig.Docker.ContainerIdAsync(spec.Name));
		Assert.True((await second.RunAsync(new SandboxCommand("true", [], second.WorkPath), Token)).Succeeded, "The remade sandbox does not run commands.");
	}

	[Theory(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(SandboxRole.Gates)]
	[InlineData(SandboxRole.Review)]
	[InlineData(SandboxRole.Resolve)]
	public async Task DisposeAsync_RoundSandbox_IsRemoved(SandboxRole role)
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, role);
		ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);

		await sandbox.DisposeAsync();

		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), $"A {role} sandbox is fresh for each round, so it must go when the round ends.");
	}
}
