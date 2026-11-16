using System.Diagnostics;
using System.Text.RegularExpressions;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>Cancelling <see cref="DockerSandboxes.EnsureImageAsync"/> in the middle of a build with a real engine: the call throws, the engine stops the build, and no tag moves, so a first build's image never appears and a rebuild leaves the images the project had under their tags. The first build's one step sleeps for a shorter time than the first test watches, so a build that kept running after the command was killed would finish and show its image while the test looks.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesImageCancelTests
{
	private const string StepOutput = "pyxie-test-cancel-step-running";
	private const int StepSeconds = 20;

	private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan s_watch = TimeSpan.FromSeconds(30);

	// A step's own output has a timestamp in front of it; the line that shows the command itself does not.
	private static readonly Regex s_stepOutput = new($@"^#\d+ \d+\.\d+ {StepOutput}\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

	// Any step's own output, which in the base image's build first comes from its package update.
	private static readonly Regex s_anyStepOutput = new(@"^#\d+ \d+\.\d+ \S", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

	private readonly DockerImageFixture _image;

	public DockerSandboxesImageCancelTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_CancelledMidBuild_ThrowsAndNoImageAppears()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");
		string dockerfile = $"ARG PYXIE_BASE\nFROM ${{PYXIE_BASE}}\nRUN echo {StepOutput} && sleep {StepSeconds}\n";
		string tag = DockerImages.ProjectTag(rig.ProjectKey, dockerfile);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		int starts = 0;
		SandboxImageRequest request = new(rig.ProjectKey, dockerfile, Rebuild: false, log) { BuildStarting = () => Interlocked.Increment(ref starts) };
		Task<ServiceResult<string>> build = rig.Sandboxes.EnsureImageAsync(request, stop.Token);

		await WaitUntilTheStepRunsAsync(build, log, s_stepOutput);
		await stop.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
		Assert.Equal(1, Volatile.Read(ref starts));
		await AssertNoImageAppearsAsync(rig, tag);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_RebuildCancelledMidBuild_ThrowsAndLeavesThePreviousImagesUnderTheirTags()
	{
		await using DockerSandboxRig rig = new(_image);
		string dockerfile = DockerSandboxRig.TinyDockerfile("previous");
		string tag = await rig.BuildImageAsync(dockerfile, Token);
		string previous = await rig.Docker.ImageIdAsync(tag);
		string baseImage = await rig.Docker.ImageIdAsync(DockerImages.BaseTag);

		// A log of its own, so the wait never reads what the first build wrote before the rebuild empties it.
		string log = Path.Combine(rig.Folder, "rebuild.log");
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		int starts = 0;
		SandboxImageRequest request = new(rig.ProjectKey, dockerfile, Rebuild: true, log) { BuildStarting = () => Interlocked.Increment(ref starts) };
		Task<ServiceResult<string>> build = rig.Sandboxes.EnsureImageAsync(request, stop.Token);

		// A rebuild builds the base image afresh first, which takes minutes, so the cancel lands in that build.
		try
		{
			await WaitUntilTheStepRunsAsync(build, log, s_anyStepOutput);
		}
		finally
		{
			// Stopped even when the wait fails, or the rebuild would go on in the engine after the test and move the shared base image's tag.
			await stop.CancelAsync();
			await Task.WhenAny(build);
		}

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
		Assert.Equal(1, Volatile.Read(ref starts));
		await AssertThePreviousImagesStayAsync(rig, tag, previous, baseImage);
	}

	private static async Task WaitUntilTheStepRunsAsync(Task<ServiceResult<string>> build, string log, Regex output)
	{
		Stopwatch watch = Stopwatch.StartNew();

		while (!output.IsMatch(await ReadLogAsync(log)))
		{
			if (build.IsCompleted)
			{
				ServiceResult<string> ended = await build;
				Assert.Fail($"The build ended before its step ran: {ended.Error}\n{await ReadLogAsync(log)}");
			}

			Assert.True(watch.Elapsed < RuntimeTestTimeouts.Wait, $"The build's step did not run within {RuntimeTestTimeouts.Wait}.\n{await ReadLogAsync(log)}");

			await Task.Delay(s_poll, Token);
		}
	}

	private static async Task AssertNoImageAppearsAsync(DockerSandboxRig rig, string tag)
	{
		Stopwatch watch = Stopwatch.StartNew();

		do
		{
			Assert.False(await rig.Docker.ImageExistsAsync(tag), $"The image appeared {watch.Elapsed} after the build was cancelled, so the engine did not stop it.");

			await Task.Delay(s_poll, Token);
		}
		while (watch.Elapsed < s_watch);

		Assert.Empty(await rig.Docker.ProjectImageIdsAsync(rig.ProjectKey));
	}

	// The cancel lands in the base image's fresh build, which would take minutes to finish, so watching for a build that kept running would see nothing; the first test shows the engine stops a killed build.
	private static async Task AssertThePreviousImagesStayAsync(DockerSandboxRig rig, string tag, string previous, string baseImage)
	{
		Assert.True(await rig.Docker.ImageExistsAsync(tag), "The project's image lost its tag when the rebuild was cancelled.");
		string image = await rig.Docker.ImageIdAsync(tag);
		Assert.True(image == previous, $"The project's tag moved from {previous} to {image} when the rebuild was cancelled.");
		Assert.True(await rig.Docker.ImageExistsAsync(DockerImages.BaseTag), "Pyxie's base image lost its tag when the rebuild of it was cancelled.");
		string baseNow = await rig.Docker.ImageIdAsync(DockerImages.BaseTag);
		Assert.True(baseNow == baseImage, $"Pyxie's base tag moved from {baseImage} to {baseNow} when the rebuild of it was cancelled.");

		IReadOnlySet<string> left = await rig.Docker.ProjectImageIdsAsync(rig.ProjectKey);
		Assert.True(left.SetEquals([previous]), $"The project's images after the cancelled rebuild were [{string.Join(", ", left)}], not just the one it had ({previous}).");
	}

	// The build still has the log open for writing.
	private static async Task<string> ReadLogAsync(string path)
	{
		if (!File.Exists(path))
		{
			return string.Empty;
		}

		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using StreamReader reader = new(stream);

		return await reader.ReadToEndAsync(Token);
	}
}
