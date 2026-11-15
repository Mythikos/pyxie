using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>A running <see cref="DockerSandbox"/>: commands run as the unprivileged <c>node</c> user with their standard input and log, a failing command is an exit rather than a broken sandbox, files are written, read, and deleted inside it, the input folder is shared read-only and the output folder writable, and leftover processes can be ended.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_Command_RunsAsTheNodeUserInTheFolderItNames()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "id -un; id -u; pwd; echo \"$HOME\""], sandbox.WorkPath), Token);

		Assert.True(result.Succeeded, result.Output);
		string[] lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.Equal("node", lines[0]);
		Assert.True(lines[1] != "0", "Commands in a sandbox must never run as root.");
		Assert.Equal(new[] { "/work", "/home/node" }, lines[2..]);
		Assert.Equal(("/work", "/pyxie/in", "/pyxie/out"), (sandbox.WorkPath, sandbox.InputPath, sandbox.OutputPath));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_StandardInput_ReachesTheCommand()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Review), Token);

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("cat", [], sandbox.WorkPath) { StandardInput = "Read the briefing.\nThen write the result." }, Token);

		Assert.True(result.Succeeded, result.Output);
		Assert.Equal("Read the briefing.\nThen write the result.", result.StandardOutput);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_Environment_ReachesTheCommandAsWritten()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);
		Dictionary<string, string> environment = new()
		{
			["PYXIE_TEST_SPACES"] = "two words  and a trailing space ",
			["PYXIE_TEST_EQUALS"] = "a=b=c",
			["PYXIE_TEST_QUOTES"] = "say \"hi\" and 'bye' and $HOME and `ticks` and \\",
		};

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "for name in PYXIE_TEST_SPACES PYXIE_TEST_EQUALS PYXIE_TEST_QUOTES; do printf '[%s]\n' \"$(printenv \"$name\")\"; done"], sandbox.WorkPath) { Environment = environment }, Token);

		Assert.True(result.Succeeded, result.Output);
		Assert.Equal(["[two words  and a trailing space ]", "[a=b=c]", "[say \"hi\" and 'bye' and $HOME and `ticks` and \\]"], result.StandardOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_Environment_WinsOverTheImagesAndIsForThatCommandOnly()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);
		const string imageVariable = "DISABLE_AUTOUPDATER";
		SandboxCommand read = new("printenv", [imageVariable], sandbox.WorkPath);

		ProcessResult changed = await sandbox.RunAsync(read with { Environment = new Dictionary<string, string> { [imageVariable] = "overridden" } }, Token);
		ProcessResult next = await sandbox.RunAsync(read, Token);

		Assert.Equal("overridden", changed.StandardOutput.Trim());
		Assert.Equal("1", next.StandardOutput.Trim());
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_EnvironmentNameWithAnEqualsSign_IsRefused()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);

		await Assert.ThrowsAsync<ArgumentException>(() => sandbox.RunAsync(new SandboxCommand("true", [], sandbox.WorkPath) { Environment = new Dictionary<string, string> { ["A=B"] = "c" } }, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_NonZeroExit_IsAnExitAndLeavesTheSandboxUsable()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "echo failing; exit 3"], sandbox.WorkPath), Token);

		Assert.Equal((ProcessOutcome.Exited, 3), (result.Outcome, result.ExitCode));
		Assert.Contains("failing", result.Output, StringComparison.Ordinal);
		Assert.False(sandbox.IsSpoiled, "A command that exits with an error code ends by itself, so the sandbox can be used again.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_MissingProgram_CouldNotStart()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("pyxie-no-such-program", [], sandbox.WorkPath), Token);

		Assert.True(result.Outcome == ProcessOutcome.NotStarted, $"A program the sandbox lacks never started, but the run ended {result.Outcome}, exit code {result.ExitCode}: {result.Output}");
		Assert.False(string.IsNullOrWhiteSpace(result.StartError), "A command that could not start says why.");
		Assert.False(sandbox.IsSpoiled, "Nothing ran, so nothing can be left running.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_LogPath_KeepsTheOutputOnTheHost()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);
		string log = Path.Combine(rig.Folder, "runs", "round-1.log");

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "echo pyxie-to-the-log"], sandbox.WorkPath) { LogPath = log }, Token);

		Assert.True(result.Succeeded, result.Output);
		Assert.Contains("pyxie-to-the-log", File.ReadAllText(log), StringComparison.Ordinal);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WriteFileAsync_ThenReadFileAsync_GivesTheTextBack()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker), Token);
		string path = "/work/notes/deep/briefing.md";
		string text = "Quotes \" and ' and `ticks` and $HOME and \\ stay as written.\nhéllo — ✓\n\nlast line";

		await sandbox.WriteFileAsync(path, text, Token);

		Assert.Equal(text, await sandbox.ReadFileAsync(path, 10_000, Token));
		ProcessResult owner = await sandbox.RunAsync(new SandboxCommand("stat", ["-c", "%U", path], sandbox.WorkPath), Token);
		Assert.Equal("node", owner.StandardOutput.Trim());
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ReadFileAsync_LineBreaks_ComeBackAsWritten()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Review), Token);
		await sandbox.WriteFileAsync("/work/lines.txt", "windows\r\nunix\nlast\n", Token);

		Assert.Equal("windows\r\nunix\nlast\n", await sandbox.ReadFileAsync("/work/lines.txt", 1000, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ReadFileAsync_MaxLength_ReadsOnlyTheBeginning()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Review), Token);
		await sandbox.WriteFileAsync("/work/digits.txt", "0123456789", Token);

		Assert.Equal("0123", await sandbox.ReadFileAsync("/work/digits.txt", 4, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ReadFileAsync_NoSuchFile_IsNull()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Review), Token);

		Assert.Null(await sandbox.ReadFileAsync("/work/.pyxie/result.json", 1000, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task DeleteFileAsync_File_IsGoneAndDeletingItAgainIsFine()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Review), Token);
		await sandbox.WriteFileAsync("/work/.pyxie/result.json", "{}", Token);

		await sandbox.DeleteFileAsync("/work/.pyxie/result.json", Token);
		await sandbox.DeleteFileAsync("/work/.pyxie/result.json", Token);

		Assert.Null(await sandbox.ReadFileAsync("/work/.pyxie/result.json", 1000, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_InputAndOutputFolders_AreSharedWithTheHostAndInputIsReadOnly()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker), Token);
		await File.WriteAllTextAsync(Path.Combine(sandbox.InputFolder, "hand-in.txt"), "from pyxie", Token);

		string? handedIn = await sandbox.ReadFileAsync($"{sandbox.InputPath}/hand-in.txt", 1000, Token);
		await sandbox.WriteFileAsync($"{sandbox.OutputPath}/hand-out.txt", "from the sandbox", Token);

		Assert.Equal("from pyxie", handedIn);
		Assert.Equal("from the sandbox", await File.ReadAllTextAsync(Path.Combine(sandbox.OutputFolder, "hand-out.txt"), Token));
		await Assert.ThrowsAsync<SandboxException>(() => sandbox.WriteFileAsync($"{sandbox.InputPath}/planted.txt", "from the sandbox", Token));
		Assert.False(File.Exists(Path.Combine(sandbox.InputFolder, "planted.txt")), "The sandbox wrote into its input folder, which it must only read.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EndProcessesAsync_ProcessLeftRunning_IsEndedAndTheSandboxStaysUsable()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker), Token);
		ProcessResult started = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", "nohup sleep 600 >/dev/null 2>&1 &"], sandbox.WorkPath), Token);
		Assert.True(started.Succeeded, started.Output);
		Assert.True(await SandboxProcessProbe.IsRunningAsync(sandbox, "sleep"), "The background process the test started is not running.");

		await sandbox.EndProcessesAsync(Token);

		Assert.False(await SandboxProcessProbe.IsRunningAsync(sandbox, "sleep"), "The process the agent left running is still running.");
		Assert.True((await sandbox.RunAsync(new SandboxCommand("true", [], sandbox.WorkPath), Token)).Succeeded, "The sandbox broke when its processes were ended.");
	}
}
