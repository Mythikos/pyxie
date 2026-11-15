using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessRunner"/> with a Windows batch file as the command, which it can only start through cmd.exe (npm installs agent harnesses such as Codex as <c>.cmd</c> files). The batch files here run the fake harness with <c>%*</c>, so the arguments it records are exactly what survived cmd.exe.</summary>
public sealed class ProcessRunnerBatchFileTests
{
	public static TheoryData<string> Arguments { get; } =
	[
		"plain",
		"two words",
		"",
		"a&b",
		"a|b",
		"a<b>c",
		"a^b",
		"(parenthesised words)",
		"a,b;c=d",
		"tab\tseparated",
		@"C:\dir\",
		@"C:\dir with space\",
		@"C:\dir with space\\",
		"-x --long=value",
		"cmd metacharacters & | < > ^ ( ) , ; = inside words",
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	[MemberData(nameof(Arguments))]
	public async Task RunAsync_AFileWithAnArgumentCmdGivesSpecialMeaning_PassesItIntact(string argument)
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string script = await BatchScript.WriteFakeHarnessLauncherAsync(scratch.Folder, "launcher.cmd");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(script, ["--record", record, "--note", argument], scratch.Folder), Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The batch file did not succeed: {result.Outcome}, exit code {result.ExitCode}, {result.StartError}{result.Output}");
		Assert.Equal(["--record", record, "--note", argument], (await FakeHarnessRecord.ReadSingleAsync(record)).Arguments);
	}

	[Theory(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	[InlineData("launcher.cmd")]
	[InlineData("launcher.bat")]
	[InlineData("LAUNCHER.CMD")]
	public async Task RunAsync_CmdAndBatFiles_BothRun(string fileName)
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string script = await BatchScript.WriteFakeHarnessLauncherAsync(scratch.Folder, fileName);
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(script, ["--record", record], scratch.Folder), Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The batch file did not succeed: {result.Output}");
		Assert.True(File.Exists(record), "The batch file must have run the fake harness.");
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_ACommandNamedWithoutItsExtension_RunsTheBatchFileItResolvesTo()
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		await BatchScript.WriteFakeHarnessLauncherAsync(scratch.Folder, "launcher.cmd");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(scratch.Combine("launcher"), ["--record", record], scratch.Folder), Token).WithinLimitAsync();

		Assert.True(result.Succeeded, $"The batch file did not succeed: {result.Outcome}, {result.StartError}{result.Output}");
		Assert.True(File.Exists(record), "The batch file must have run the fake harness.");
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task RunAsync_AFile_GetsStandardInputAndReturnsTheExitCodeOfItsLastCommand()
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string script = await BatchScript.WriteFakeHarnessLauncherAsync(scratch.Folder, "launcher.cmd");
		using ProcessRunner runner = new(TimeProvider.System);
		ProcessRequest request = new(script, ["--record", record, "--exit", "7"], scratch.Folder) { StandardInput = "from standard input" };

		ProcessResult result = await runner.RunAsync(request, Token).WithinLimitAsync();

		Assert.Equal(ProcessOutcome.Exited, result.Outcome);
		Assert.Equal(7, result.ExitCode);
		Assert.Equal("from standard input", (await FakeHarnessRecord.ReadSingleAsync(record)).Input);
	}

	[Theory(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	[InlineData("100%")]
	[InlineData("%PATH%")]
	[InlineData("wow!")]
	[InlineData("say \"hi\"")]
	[InlineData("two\nlines")]
	public async Task RunAsync_AFileWithAnArgumentCmdCannotPassSafely_IsNotStartedAndNeverRuns(string argument)
	{
		await using ScratchFolder scratch = new();
		string record = scratch.Combine("record.txt");
		string script = await BatchScript.WriteFakeHarnessLauncherAsync(scratch.Folder, "launcher.cmd");
		using ProcessRunner runner = new(TimeProvider.System);

		ProcessResult result = await runner.RunAsync(new ProcessRequest(script, ["--record", record, "--note", argument], scratch.Folder), Token).WithinLimitAsync();

		Assert.Equal(ProcessOutcome.NotStarted, result.Outcome);
		Assert.Equal(-1, result.ExitCode);
		Assert.Contains("launcher.cmd", result.StartError, StringComparison.Ordinal);
		Assert.Contains("cannot be passed", result.StartError, StringComparison.Ordinal);
		Assert.False(File.Exists(record), "A refused batch file must not run at all.");
	}
}
