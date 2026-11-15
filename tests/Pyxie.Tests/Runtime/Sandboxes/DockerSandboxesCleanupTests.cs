using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes"/> cleaning up after cards with a real engine: removing a card takes its containers and volumes and nothing of another card or project, the cards a project left something of are listed from its containers and volumes, and leftover gate and review sandboxes go while workers and their volumes stay. Two rigs stand for two projects on one machine.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesCleanupTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesCleanupTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveCardAsync_Card_RemovesItsContainersAndVolumesAndNothingElse()
	{
		await using DockerSandboxRig rig = new(_image);
		await using DockerSandboxRig otherProject = new(_image);
		SandboxSpec worker = rig.Spec(1, SandboxRole.Worker);
		SandboxSpec gates = rig.Spec(1, SandboxRole.Gates);
		SandboxSpec otherCard = rig.Spec(2, SandboxRole.Worker);
		SandboxSpec sameCardOtherProject = otherProject.Spec(1, SandboxRole.Worker);
		await using ISandbox workerSandbox = await rig.Sandboxes.StartAsync(worker, Token);
		await using ISandbox gatesSandbox = await rig.Sandboxes.StartAsync(gates, Token);
		await using ISandbox otherCardSandbox = await rig.Sandboxes.StartAsync(otherCard, Token);
		await using ISandbox otherProjectSandbox = await otherProject.Sandboxes.StartAsync(sameCardOtherProject, Token);

		await rig.Sandboxes.RemoveCardAsync(rig.ProjectKey, 1, Token);

		await AssertGoneAsync(rig, worker);
		await AssertGoneAsync(rig, gates);
		await AssertThereAsync(rig, otherCard);
		await AssertThereAsync(otherProject, sameCardOtherProject);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ListCardsAsync_ContainersAndVolumes_ListsTheProjectsCardsOnly()
	{
		await using DockerSandboxRig rig = new(_image);
		await using DockerSandboxRig otherProject = new(_image);
		await using ISandbox running = await rig.Sandboxes.StartAsync(rig.Spec(3, SandboxRole.Worker), Token);
		await using ISandbox review = await rig.Sandboxes.StartAsync(rig.Spec(6, SandboxRole.Review), Token);
		SandboxSpec volumesOnly = rig.Spec(4, SandboxRole.Worker);
		ISandbox kept = await rig.Sandboxes.StartAsync(volumesOnly, Token);
		await kept.DisposeAsync();
		await rig.Docker.RunCheckedAsync("rm", "--force", volumesOnly.Name);
		await using ISandbox other = await otherProject.Sandboxes.StartAsync(otherProject.Spec(9, SandboxRole.Worker), Token);

		IReadOnlySet<int> cards = await rig.Sandboxes.ListCardsAsync(rig.ProjectKey, Token);

		Assert.Equal(new[] { 3, 4, 6 }, cards.Order());
		Assert.Equal(new[] { 9 }, await otherProject.Sandboxes.ListCardsAsync(otherProject.ProjectKey, Token));
		Assert.Empty(await rig.Sandboxes.ListCardsAsync(SandboxNames.ProjectKey(Path.Combine(rig.Folder, "never-used")), Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RemoveLeftoverRoundsAsync_Project_RemovesGateReviewAndResolveSandboxesAndKeepsWorkers()
	{
		await using DockerSandboxRig rig = new(_image);
		await using DockerSandboxRig otherProject = new(_image);
		SandboxSpec worker = rig.Spec(1, SandboxRole.Worker);
		SandboxSpec gates = rig.Spec(1, SandboxRole.Gates);
		SandboxSpec review = rig.Spec(2, SandboxRole.Review);
		SandboxSpec resolve = rig.Spec(3, SandboxRole.Resolve);
		SandboxSpec otherGates = otherProject.Spec(1, SandboxRole.Gates);
		await using ISandbox workerSandbox = await rig.Sandboxes.StartAsync(worker, Token);
		await using ISandbox gatesSandbox = await rig.Sandboxes.StartAsync(gates, Token);
		await using ISandbox reviewSandbox = await rig.Sandboxes.StartAsync(review, Token);
		await using ISandbox resolveSandbox = await rig.Sandboxes.StartAsync(resolve, Token);
		await using ISandbox otherGatesSandbox = await otherProject.Sandboxes.StartAsync(otherGates, Token);

		await rig.Sandboxes.RemoveLeftoverRoundsAsync(rig.ProjectKey, Token);

		Assert.False(await rig.Docker.ContainerExistsAsync(gates.Name), "A gate sandbox left by a round Pyxie stopped part-way must be removed.");
		Assert.False(await rig.Docker.ContainerExistsAsync(review.Name), "A review sandbox left by a round Pyxie stopped part-way must be removed.");
		Assert.False(await rig.Docker.ContainerExistsAsync(resolve.Name), "A resolve sandbox left by a round Pyxie stopped part-way must be removed.");
		await AssertThereAsync(rig, worker);
		Assert.True(await rig.Docker.VolumeExistsAsync(gates.Volumes[0].Name), "The gates' home volume keeps the card's package caches and must stay.");
		Assert.True(await otherProject.Docker.ContainerExistsAsync(otherGates.Name), "Another project's gate sandbox was removed.");
	}

	private static async Task AssertGoneAsync(DockerSandboxRig rig, SandboxSpec spec)
	{
		Assert.False(await rig.Docker.ContainerExistsAsync(spec.Name), $"{spec.Name} is still there.");
		foreach (SandboxVolume volume in spec.Volumes)
		{
			Assert.False(await rig.Docker.VolumeExistsAsync(volume.Name), $"The volume {volume.Name} is still there.");
		}
	}

	private static async Task AssertThereAsync(DockerSandboxRig rig, SandboxSpec spec)
	{
		Assert.True(await rig.Docker.ContainerExistsAsync(spec.Name), $"{spec.Name} was removed.");
		foreach (SandboxVolume volume in spec.Volumes)
		{
			Assert.True(await rig.Docker.VolumeExistsAsync(volume.Name), $"The volume {volume.Name} was removed.");
		}
	}
}
