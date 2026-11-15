using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A person can push to the hub the cards are merged into, and the cards that start afterwards build on what was pushed.</summary>
public sealed class PipelineOrchestratorPushedCommitTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardStartedAfterAPersonPushedToMain_BuildsOnThePushedCommitAndMergesItsWorkOnTopOfIt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(harness.Clone);
		string pushed = await person.CommitFileAsync("pushed.txt", "from a person");
		await person.PushAsync("main");
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Add the feature", worker);

		await harness.StartPipelineAsync();
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);
		await harness.StopOrchestratorAsync();

		Assert.Equal("Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.True(await harness.Git.IsAncestorAsync(harness.Clone.Folder, pushed, "main", Token), "The merged target branch must contain the commit the person pushed.");
		Assert.Equal("from a person", await harness.Clone.ReadFileAtAsync("main", "pushed.txt"));
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}
}
