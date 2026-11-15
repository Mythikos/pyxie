using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes.EnsureImageAsync"/> with a real engine: a project's image is built on Pyxie's base image from the project's Dockerfile, an image that is there is never built again, and a Dockerfile that fails says where its log is. The base image itself is built by <see cref="DockerImageFixture"/> the first time.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesImageTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesImageTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_DefaultDockerfile_BuildsTheProjectImageOnTheBaseImage()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");

		ServiceResult<string> result = await rig.Sandboxes.EnsureImageAsync(new SandboxImageRequest(rig.ProjectKey, SandboxDefaults.Dockerfile, Rebuild: false, log), Token);

		Assert.True(result.Succeeded, $"{result.Error}\n{ReadLog(log)}");
		Assert.NotNull(result.Value);
		Assert.Equal(DockerImages.ProjectTag(rig.ProjectKey, SandboxDefaults.Dockerfile), result.Value);
		Assert.True(ReadLog(log).Length > 0, "Building the project's image left no log.");
		await rig.Docker.RunCheckedAsync("image", "inspect", "--format", "{{.Id}}", DockerImages.BaseTag);
		string entrypoint = await rig.Docker.RunCheckedAsync("image", "inspect", "--format", "{{json .Config.Entrypoint}}", result.Value);
		Assert.True(entrypoint.Contains("pyxie-sandbox", StringComparison.Ordinal), $"The project's image is not built on Pyxie's base image: its entrypoint is {entrypoint}.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_ImageAlreadyThere_DoesNotBuildItAgain()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");
		SandboxImageRequest request = new(rig.ProjectKey, SandboxDefaults.Dockerfile, Rebuild: false, log);
		ServiceResult<string> first = await rig.Sandboxes.EnsureImageAsync(request, Token);
		Assert.True(first.Succeeded, $"{first.Error}\n{ReadLog(log)}");
		File.Delete(log);

		// A new instance knows nothing of what the first one built, so only the engine can tell it the image is there.
		using ProcessRunner runner = new(TimeProvider.System);
		ServiceResult<string> second = await new DockerSandboxes(runner, TimeProvider.System).EnsureImageAsync(request, Token);

		Assert.Equal(first, second);
		Assert.False(File.Exists(log), "The image was there, but it was built again.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_DockerfileThatFails_FailsAndPointsAtTheLog()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");
		string dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo pyxie-test-broken-step && exit 3\n";

		ServiceResult<string> result = await rig.Sandboxes.EnsureImageAsync(new SandboxImageRequest(rig.ProjectKey, dockerfile, Rebuild: false, log), Token);

		Assert.False(result.Succeeded, "A Dockerfile whose step fails cannot give an image.");
		Assert.Contains(log, result.Error, StringComparison.Ordinal);
		Assert.Contains("pyxie-test-broken-step", ReadLog(log), StringComparison.Ordinal);
		Assert.False((await rig.Docker.RunAsync("image", "inspect", DockerImages.ProjectTag(rig.ProjectKey, dockerfile))).Succeeded, "A failed build left an image behind.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_DefaultImage_HasTheToolsAgentsAndGatesUse()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);
		string script = "for tool in git node npm python3 curl claude codex opencode; do command -v \"$tool\" >/dev/null || { echo \"missing $tool\"; exit 1; }; done";

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", script], sandbox.WorkPath), Token);

		Assert.True(result.Succeeded, $"The base image lacks a tool: {result.Output}");
	}

	private static string ReadLog(string path)
	{
		return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
	}
}
