using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary>The fake harness's <c>--git</c> and <c>--wait-for</c> options, which tests use to play an agent that runs git (a merge agent that aborts, or commits twice) and to hold an agent until the test says so.</summary>
public sealed class FakeHarnessCommandTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithGitCommands_RunsThemInOrderInTheRepositoryAfterTheFilesAreWritten()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--git", "init --quiet", "--write", "a.txt=written first", "--git", "add --all", "--git", "-c user.name=Agent -c user.email=agent@localhost commit --quiet --message \"two words\"", "--git", "log --format=%s", "--git", "show --name-only --format= HEAD");

		RoundVerdict verdict = await rig.RunAsync(agent);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		string log = (await File.ReadAllTextAsync(rig.LogPath, Token)).ReplaceLineEndings("\n");
		Assert.Contains("two words\n", log, StringComparison.Ordinal);
		Assert.Contains("a.txt\n", log, StringComparison.Ordinal);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WhenAGitCommandFails_EndsTheRunWithItsExitCodeAndWritesNoResult()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--git", "not-a-git-command", "--outcome", "resolved");

		RoundVerdict verdict = await rig.RunAsync(agent);

		Assert.Equal(RoundResult.Errored, verdict.Result);
		Assert.Equal("Agent 'Agent' exited with code 1.", verdict.Note);
		Assert.Contains("not-a-git-command", await File.ReadAllTextAsync(rig.LogPath, Token), StringComparison.Ordinal);
		Assert.False(File.Exists(rig.ResultFile));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_WithAWaitFor_DoesNothingUntilTheFileExists()
	{
		await using HarnessRig rig = await HarnessRig.CreateAsync();
		string release = rig.Combine("release");
		string written = Path.Combine(rig.RepositoryFolder, "after.txt");
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Agent", HarnessKind.Other, "--wait-for", release, "--write", "after.txt=released");

		Task<RoundVerdict> run = rig.RunAsync(agent);
		await Task.Delay(TimeSpan.FromSeconds(1.5), Token);

		Assert.False(run.IsCompleted, "The agent must wait for the file.");
		Assert.False(File.Exists(written), "The agent must write nothing before the file exists.");

		await File.WriteAllTextAsync(release, "go", Token);
		RoundVerdict verdict = await run;

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal("released", await File.ReadAllTextAsync(written, Token));
	}
}
