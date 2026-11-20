using System.Text;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessRunner"/> and the files it leaves in the card's repository in the sandbox for the agent: the briefing is written first, through the sandbox, and an old result file is removed before the agent starts, so only what this round's agent writes is read back.</summary>
public sealed class HarnessRunnerBriefingTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WritesTheBriefingIntoTheRepositoryExactlyAsUtf8WithoutAByteOrderMark()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string briefing = "# Card\r\n\r\nCafé ✓ — the task\nsecond line  \n";

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other), briefing: briefing);

		byte[] written = await File.ReadAllBytesAsync(rig.BriefingFile, Token);
		Assert.Equal(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(briefing), written);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_CreatesPyxiesFolderInTheRepositoryForTheBriefingWhenItIsMissing()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		Directory.CreateDirectory(rig.RepositoryFolder);
		Assert.False(Directory.Exists(rig.PyxieFolder), "The test starts with a repository that has no .pyxie folder.");

		RoundVerdict verdict = await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other));

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.True(File.Exists(rig.BriefingFile), "The briefing must be written to the repository's .pyxie folder.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ReplacesAnEarlierBriefing()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		await rig.WriteRepositoryFileAsync(PipelineContract.BriefingFile, "the briefing of an earlier round, which is much longer than the new one");

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other), briefing: "new");

		Assert.Equal("new", await File.ReadAllTextAsync(rig.BriefingFile, Token));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithoutABriefing_WritesAnEmptyFile()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();

		await rig.RunAsync(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other), briefing: null);

		Assert.Equal(string.Empty, await File.ReadAllTextAsync(rig.BriefingFile, Token));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_RemovesAStaleResultFileSoAnAgentThatWritesNoneIsNotBelieved()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		await rig.WriteRepositoryFileAsync(PipelineContract.ResultFile, """{"outcome": "completed", "note": "from an earlier round"}""");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--no-result");

		RoundVerdict verdict = await rig.RunAsync(agent);

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal($"The agent did not write {PipelineContract.ResultFile}.", verdict.Note);
		Assert.False(File.Exists(rig.ResultFile), "The earlier round's result file must be gone.");
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_GoesThroughTheSandbox_WritesTheBriefingDeletesTheOldResultRunsTheAgentThenReadsTheResult()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		RecordingSandbox recorder = new(rig.Sandbox);
		string briefingPath = SandboxPaths.Combine(rig.RepositoryPath, PipelineContract.BriefingFile);
		string resultPath = SandboxPaths.Combine(rig.RepositoryPath, PipelineContract.ResultFile);

		RoundVerdict verdict = await rig.StartAsync(HarnessRig.Work(FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other)), TimeSpan.FromMinutes(2), Token, recorder).WithinLimitAsync();

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		string[] expected =
		[
			$"write {briefingPath}",
			$"delete {resultPath}",
			$"run {FakeHarnessAgent.Command} in {rig.RepositoryPath}",
			$"read {resultPath} up to {HarnessResultFile.MaxLength + 1}",
		];
		Assert.Equal(expected, recorder.Calls);
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_HasTheBriefingInPlaceAndNoResultFileWhenTheAgentStarts()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		await rig.WriteRepositoryFileAsync(PipelineContract.ResultFile, """{"outcome": "errored", "note": "stale"}""");

		// The agent copies what it finds when it starts, then writes its own result.
		string script = await BatchScript.WriteAsync(rig.Folder, "agent.cmd", @"@copy /y .pyxie\briefing.md seen-briefing.md >nul", @"@if exist .pyxie\result.json (echo present> seen-result.txt) else (echo absent> seen-result.txt)", @"@echo {""outcome"":""completed"",""note"":""ran""}> .pyxie\result.json");
		AgentSnapshot agent = new("Scripted", HarnessKind.Other, script, string.Empty, AgentAuthentication.None);

		RoundVerdict verdict = await rig.RunAsync(agent, briefing: "the briefing the agent reads");

		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "ran", null), verdict);
		Assert.Equal("the briefing the agent reads", await File.ReadAllTextAsync(Path.Combine(rig.RepositoryFolder, "seen-briefing.md"), Token));
		Assert.Equal("absent", (await File.ReadAllTextAsync(Path.Combine(rig.RepositoryFolder, "seen-result.txt"), Token)).Trim());
	}
}
