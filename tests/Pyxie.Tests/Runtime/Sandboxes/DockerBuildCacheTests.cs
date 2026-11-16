using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>How <see cref="DockerImages"/> picks and prunes Pyxie's build cache, against a real engine's cache and what <c>docker buildx du --format json</c> really prints. The steps of a test image are its own: a <c>RUN</c> with Pyxie's shell, a <c>WORKDIR</c>, and a <c>RUN</c> with a shell of the image's own, the last two recording no Pyxie shell. Only the test image's own entries are ever pruned, because the cache is shared by every project on the machine.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerBuildCacheTests
{
	private readonly DockerImageFixture _image;

	public DockerBuildCacheTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task UnusedBuildCache_StepsOfAnImageThatIsThere_AreNotPicked()
	{
		const string Word = "pyxie-test-cache-in-use";
		await using DockerSandboxRig rig = new(_image);
		await rig.BuildImageAsync(Dockerfile(Word), Token);

		IReadOnlyList<BuildCacheEntry> cache = await CacheAsync(rig, Word);

		IReadOnlyList<string> steps = StepsOf(cache, Word);
		Assert.Equal(3, steps.Count);
		Assert.Empty(DockerImages.UnusedBuildCache(cache).Intersect(steps));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task UnusedBuildCache_StepsOfARemovedImage_AreAllPickedAndOnePruneRemovesThem()
	{
		const string Word = "pyxie-test-cache-removed";
		await using DockerSandboxRig rig = new(_image);
		string tag = await rig.BuildImageAsync(Dockerfile(Word), Token);
		await rig.Docker.RunCheckedAsync("image", "rm", tag);

		IReadOnlyList<BuildCacheEntry> cache = await CacheAsync(rig, Word);
		IReadOnlyList<string> steps = StepsOf(cache, Word);
		IReadOnlyList<string> picked = [.. DockerImages.UnusedBuildCache(cache).Where(steps.Contains)];

		Assert.Equal(3, steps.Count);
		Assert.Equal(steps.Order(StringComparer.Ordinal), picked.Order(StringComparer.Ordinal));
		Assert.Single(DockerImages.PruneFilters(picked, int.MaxValue));

		await rig.Docker.RunCheckedAsync("builder", "prune", "--force", "--filter", DockerImages.PruneFilters(picked, int.MaxValue)[0]);

		Assert.Empty(StepsOf(await CacheAsync(rig, Word), Word));
	}

	// A step with Pyxie's shell, then two that record none: a WORKDIR, and a step run with a shell the image sets itself.
	private static string Dockerfile(string word)
	{
		return $"ARG PYXIE_BASE\nFROM ${{PYXIE_BASE}}\nRUN echo {word} > /pyxie-test-word\nWORKDIR /{word}\nSHELL [\"/bin/sh\", \"-c\"]\nRUN echo {word} > /pyxie-test-other-shell\n";
	}

	// The entries that name the word, which are the test image's whole chain: its first step is run with Pyxie's shell and names the word, and the other two are built on it and name it too. The whole cache of a busy machine is more than the test's docker command keeps.
	private static async Task<IReadOnlyList<BuildCacheEntry>> CacheAsync(DockerSandboxRig rig, string word)
	{
		return DockerImages.ParseBuildCache(await rig.Docker.RunCheckedAsync("buildx", "du", "--format", "json", "--filter", $"description~=\"{word}\""));
	}

	private static IReadOnlyList<string> StepsOf(IReadOnlyList<BuildCacheEntry> cache, string word)
	{
		return [.. cache.Where(entry => entry.Description.Contains(word, StringComparison.Ordinal)).Select(entry => entry.Id)];
	}
}
