using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> when the process cannot be started: that is a <see cref="ProcessOutcome.NotStarted"/> result with the reason, never an exception.</summary>
public sealed class ProcessRunnerStartFailureTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_AMissingCommand_IsNotStartedAndNamesTheCommand()
	{
		await using ScratchFolder scratch = new();
		string command = $"pyxie-no-such-program-{Guid.NewGuid():N}";
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(command, [], scratch.Folder), Token).WithinLimitAsync();

		AssertNotStarted(result);
		Assert.Contains(command, result.StartError, StringComparison.Ordinal);
		Assert.Contains("not found", result.StartError, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_AMissingWorkingDirectory_IsNotStartedAndNamesTheFolder()
	{
		await using ScratchFolder scratch = new();
		string missing = scratch.Combine("no-such-folder");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(FakeHarnessRequest.Create(missing), Token).WithinLimitAsync();

		AssertNotStarted(result);
		Assert.Contains(missing, result.StartError, StringComparison.Ordinal);
		Assert.Contains("does not exist", result.StartError, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task RunAsync_ABlankCommand_IsNotStarted(string command)
	{
		await using ScratchFolder scratch = new();
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(command, [], scratch.Folder), Token).WithinLimitAsync();

		AssertNotStarted(result);
	}

	[Fact]
	public async Task RunAsync_AFileThatCannotBeExecuted_IsNotStartedAndSaysWhy()
	{
		await using ScratchFolder scratch = new();
		string broken = scratch.CreateFile(OperatingSystem.IsWindows() ? "broken.exe" : "broken", "this is not a program");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(broken, [], scratch.Folder), Token).WithinLimitAsync();

		AssertNotStarted(result);
		Assert.Contains(broken, result.StartError, StringComparison.Ordinal);
		Assert.Contains("could not start", result.StartError, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_ACommandThatIsNotStarted_DoesNotTouchTheLogFile()
	{
		await using ScratchFolder scratch = new();
		string log = scratch.Combine("run.log");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest($"pyxie-no-such-program-{Guid.NewGuid():N}", [], scratch.Folder) { LogPath = log }, Token).WithinLimitAsync();

		AssertNotStarted(result);
		Assert.False(File.Exists(log), "A process that never started has nothing to log.");
	}

	private static void AssertNotStarted(ProcessResult result)
	{
		Assert.Equal(ProcessOutcome.NotStarted, result.Outcome);
		Assert.Equal(-1, result.ExitCode);
		Assert.Equal(string.Empty, result.Output);
		Assert.Equal(TimeSpan.Zero, result.Duration);
		Assert.False(result.Succeeded, "A process that never started did not succeed.");
		Assert.False(string.IsNullOrWhiteSpace(result.StartError), "A start failure must say why.");
	}
}
