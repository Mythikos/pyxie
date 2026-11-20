using System.Text.Json;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Media;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

public sealed class RoundExecutorMediaTests
{
	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData("completed", RoundResult.Succeeded)]
	[InlineData("errored", RoundResult.Errored)]
	[InlineData("blocked", RoundResult.Blocked)]
	public async Task AgentPublication_SurvivesItsRoundRegardlessOfWorkOutcome(string outcome, RoundResult expected)
	{
		await using RoundExecutorRig rig = await RoundExecutorRig.CreateAsync();
		string output = Path.Combine(WorkspaceLayout.SandboxFolder(rig.Runtime.ProjectFolder, RoundExecutorRig.CardId, SandboxNames.RoleName(SandboxRole.Worker)), "out", "media");
		string manifest = JsonSerializer.Serialize(new { version = 1, files = new[] { new { path = "files/capture.txt", name = "capture.txt" } } });
		AgentSnapshot agent = FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=updated", "--write", $"{Path.Combine(output, "files", "capture.txt")}=terminal evidence", "--write", $"{Path.Combine(output, "manifest.json")}={manifest}", "--outcome", outcome, "--note", "Round result");
		(RoundVerdict verdict, _) = await rig.RunAsync(RoundKind.Work, agent);
		Assert.Equal(expected, verdict.Result);
		MediaFile file = Assert.Single(verdict.Media);
		Assert.Equal("terminal evidence", await File.ReadAllTextAsync(MediaPaths.Object(rig.Runtime.ProjectFolder, file.Id), TestContext.Current.CancellationToken));
		Assert.Equal(RoundExecutorRig.CardId, file.CardId);
		Assert.Equal(1, file.RoundId);
		if (expected == RoundResult.Succeeded)
		{
			Assert.Equal(await rig.GetBranchTipAsync(), file.Commit);
		}
		else
		{
			Assert.Null(file.Commit);
		}
	}
}
