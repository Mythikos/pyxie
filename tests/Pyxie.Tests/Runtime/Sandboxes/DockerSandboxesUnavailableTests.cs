using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes"/> when there is no container engine to use, which no real engine can be made to show on demand: <c>docker</c> missing from <c>PATH</c>, or a stand-in <c>docker</c> (see <see cref="FakeDocker"/>) that answers like an engine that is down or runs Windows containers. Whatever the command, an engine that cannot be used is <see cref="SandboxUnavailableException"/>, never something wrong with a card. Each test changes <c>PATH</c> and puts it back, which is only safe because <see cref="ProcessEnvironmentCollection"/> never runs beside another test.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DockerSandboxesUnavailableTests
{
	private const string EngineDownError = "Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CheckAvailableAsync_DockerNotOnPath_FailsAndSaysToInstallDocker()
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope path = new("PATH", scratch.CreateSubfolder("empty"));
		using ProcessRunner runner = new(TimeProvider.System);

		ServiceResult result = await new DockerSandboxes(runner, TimeProvider.System).CheckAvailableAsync(Token);

		Assert.False(result.Succeeded, "Docker is not on PATH, so sandboxes cannot be made.");
		Assert.Contains("Install Docker Desktop", result.Error, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(nameof(DockerSandboxes.EnsureImageAsync))]
	[InlineData(nameof(DockerSandboxes.FindImageAsync))]
	[InlineData(nameof(DockerSandboxes.StartAsync))]
	[InlineData(nameof(DockerSandboxes.RemoveCardAsync))]
	[InlineData(nameof(DockerSandboxes.RemoveLeftoverRoundsAsync))]
	[InlineData(nameof(DockerSandboxes.ListCardsAsync))]
	public async Task Operation_DockerNotOnPath_ThrowsSandboxUnavailable(string operation)
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope path = new("PATH", scratch.CreateSubfolder("empty"));
		using ProcessRunner runner = new(TimeProvider.System);
		DockerSandboxes sandboxes = new(runner, TimeProvider.System);
		string key = SandboxNames.ProjectKey(scratch.Folder);

		Func<Task> run = operation switch
		{
			nameof(DockerSandboxes.EnsureImageAsync) => () => sandboxes.EnsureImageAsync(new SandboxImageRequest(key, SandboxDefaults.Dockerfile, Rebuild: false, scratch.Combine("image.log")), Token),
			nameof(DockerSandboxes.FindImageAsync) => () => sandboxes.FindImageAsync(key, SandboxDefaults.Dockerfile, Token),
			nameof(DockerSandboxes.StartAsync) => () => sandboxes.StartAsync(GateSpec(key, scratch), Token),
			nameof(DockerSandboxes.RemoveCardAsync) => () => sandboxes.RemoveCardAsync(key, 1, Token),
			nameof(DockerSandboxes.RemoveLeftoverRoundsAsync) => () => sandboxes.RemoveLeftoverRoundsAsync(key, Token),
			_ => () => sandboxes.ListCardsAsync(key, Token),
		};

		SandboxUnavailableException exception = await Assert.ThrowsAsync<SandboxUnavailableException>(run);
		Assert.Contains("docker", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CheckAvailableAsync_EngineNotRunning_FailsWithTheEngineDownMessage()
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope path = await FakeDockerFirstOnPathAsync(scratch, standardOutput: string.Empty, EngineDownError, exitCode: 1);
		using ProcessRunner runner = new(TimeProvider.System);

		ServiceResult result = await new DockerSandboxes(runner, TimeProvider.System).CheckAvailableAsync(Token);

		Assert.Equal(ServiceResult.Failure(DockerCli.EngineDownMessage), result);
	}

	// The operations whose arguments a batch file can be given (cmd.exe cannot pass quotes), so the stand-in works on Windows too.
	[Theory]
	[InlineData(nameof(DockerSandboxes.EnsureImageAsync))]
	[InlineData(nameof(DockerSandboxes.FindImageAsync))]
	[InlineData(nameof(DockerSandboxes.RemoveCardAsync))]
	[InlineData(nameof(DockerSandboxes.RemoveLeftoverRoundsAsync))]
	public async Task Operation_EngineNotRunning_ThrowsSandboxUnavailableNotSandboxException(string operation)
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope path = await FakeDockerFirstOnPathAsync(scratch, standardOutput: string.Empty, EngineDownError, exitCode: 1);
		using ProcessRunner runner = new(TimeProvider.System);
		DockerSandboxes sandboxes = new(runner, TimeProvider.System);
		string key = SandboxNames.ProjectKey(scratch.Folder);

		Func<Task> run = operation switch
		{
			nameof(DockerSandboxes.EnsureImageAsync) => () => sandboxes.EnsureImageAsync(new SandboxImageRequest(key, SandboxDefaults.Dockerfile, Rebuild: false, scratch.Combine("image.log")), Token),
			nameof(DockerSandboxes.FindImageAsync) => () => sandboxes.FindImageAsync(key, SandboxDefaults.Dockerfile, Token),
			nameof(DockerSandboxes.RemoveCardAsync) => () => sandboxes.RemoveCardAsync(key, 1, Token),
			_ => () => sandboxes.RemoveLeftoverRoundsAsync(key, Token),
		};

		SandboxUnavailableException exception = await Assert.ThrowsAsync<SandboxUnavailableException>(run);
		Assert.Equal(DockerCli.EngineDownMessage, exception.Message);
	}

	[Fact]
	public async Task CheckAvailableAsync_WindowsContainers_FailsAndSaysToSwitchToLinuxContainers()
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope path = await FakeDockerFirstOnPathAsync(scratch, "windows", standardError: string.Empty, exitCode: 0);
		using ProcessRunner runner = new(TimeProvider.System);

		ServiceResult result = await new DockerSandboxes(runner, TimeProvider.System).CheckAvailableAsync(Token);

		Assert.False(result.Succeeded, "An engine that runs Windows containers cannot run Pyxie's Linux sandboxes.");
		Assert.Contains("Linux containers", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CheckAvailableAsync_LinuxContainers_Succeeds()
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope path = await FakeDockerFirstOnPathAsync(scratch, "linux", standardError: string.Empty, exitCode: 0);
		using ProcessRunner runner = new(TimeProvider.System);

		ServiceResult result = await new DockerSandboxes(runner, TimeProvider.System).CheckAvailableAsync(Token);

		Assert.True(result.Succeeded, result.Error);
	}

	private static SandboxSpec GateSpec(string key, ScratchFolder scratch)
	{
		return new SandboxSpec(SandboxNames.Container(key, 1, SandboxRole.Gates), "pyxie-test-image", key, 1, SandboxRole.Gates, scratch.Combine("in"), scratch.Combine("out"));
	}

	// The stand-in goes in front of the real PATH, which keeps cmd.exe reachable for the batch file.
	private static async Task<EnvironmentVariableScope> FakeDockerFirstOnPathAsync(ScratchFolder scratch, string standardOutput, string standardError, int exitCode)
	{
		string folder = scratch.CreateSubfolder("fake-docker");
		await FakeDocker.WriteAsync(folder, standardOutput, standardError, exitCode);

		return new EnvironmentVariableScope("PATH", folder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
	}
}
