using Pyxie.Runtime.Media;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Media;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

[Collection(DockerSandboxCollection.Name)]
public sealed class MediaDockerTests
{
	private readonly DockerImageFixture _image;

	public MediaDockerTests(DockerImageFixture image) => _image = image;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Helper_PublishesExistingBytesAndInputRemainsReadOnly()
	{
		await using DockerSandboxRig rig = new(_image);
		await using MediaRig media = new();
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		Directory.CreateDirectory(Path.Combine(spec.InputFolder, "media"));
		await File.WriteAllTextAsync(Path.Combine(spec.InputFolder, "media", "context.txt"), "context", Token);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);
		ProcessResult published = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "printf 'capture bytes' > /work/capture.txt && pyxie-attach /work/capture.txt --caption 'Console output' && test -r /pyxie/in/media/context.txt && ! touch /pyxie/in/media/forbidden"], sandbox.WorkPath), Token);
		Assert.True(published.Succeeded, published.Output);
		await sandbox.EndProcessesAsync(Token);
		MediaCollection result = await media.Rounds.CollectAsync(media.Project, 1, 1, sandbox.OutputFolder, null, Token);
		Assert.Empty(result.Problems);
		Assert.Equal("capture.txt", Assert.Single(result.Files).Name);
		Assert.Equal("Console output", result.Files[0].Caption);
		Assert.Equal("capture bytes", await File.ReadAllTextAsync(MediaPaths.Object(media.Folder, result.Files[0].Id), Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Quiesce_PreservesVolumesAndPublishedOutput()
	{
		await using DockerSandboxRig rig = new(_image);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);
		ProcessResult write = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "printf saved > /work/kept.txt && pyxie-attach /work/kept.txt"], sandbox.WorkPath), Token);
		Assert.True(write.Succeeded, write.Output);
		await rig.Sandboxes.QuiesceCardAsync(rig.ProjectKey, 1, Token);
		Assert.True(File.Exists(Path.Combine(spec.OutputFolder, "media", "manifest.json")));
		await using ISandbox resumed = await rig.Sandboxes.StartAsync(spec, Token);
		ProcessResult read = await resumed.RunAsync(new SandboxCommand("cat", ["/work/kept.txt"], resumed.WorkPath), Token);
		Assert.True(read.Succeeded, read.Output);
		Assert.Equal("saved", read.StandardOutput);
	}
}
