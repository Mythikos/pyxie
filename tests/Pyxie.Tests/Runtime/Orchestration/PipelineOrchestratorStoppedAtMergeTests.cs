using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A card that stopped at its merge resumes there, on the branch that holds its work in the project's hub. A card whose branch is gone is blocked with a message that says so, and editing it starts it again at work. A lock on the target branch in the hub stops the merge round, which is how these tests stop a card there.</summary>
public sealed class PipelineOrchestratorStoppedAtMergeTests
{
	private const string StoppedAtMerge = "Work:Completed,Gate:Skipped,Review:Skipped,Merge:Blocked";
	private const string RestartedAndMerged = "Work:Completed,Gate:Skipped,Review:Skipped,Merge:Merged";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string TargetBranchLock(RuntimeHarness harness)
	{
		return Path.Combine(harness.Clone.GitFolder, "refs", "heads", "main.lock");
	}

	private static async Task<WorkCard> StopAtMergeAsync(RuntimeHarness harness)
	{
		Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
		WorkCard card = await harness.AddCardAsync("Add the feature", worker);
		await File.WriteAllTextAsync(TargetBranchLock(harness), string.Empty, Token);
		await harness.StartPipelineAsync();
		BoardCard stopped = await harness.WaitForStoppedAsync(card.Id, RuntimeTestTimeouts.Wait);

		Assert.Equal(StoppedAtMerge, RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal(RoundKind.Merge, stopped.Card.ResumeRound);
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync(PipelineContract.BranchName(card.Id), "feature.txt"));

		return card;
	}

	private static async Task RetryAsync(RuntimeHarness harness, int cardId)
	{
		await using AsyncServiceScope scope = harness.CreateScope();
		ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().RetryAsync(cardId, Token));
		harness.NotifyCardsChanged();
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ACardResumedAtItsMergeWhoseBranchIsGone_IsBlockedWithoutMergingAndStartsAgainAtWorkOnceItIsEdited()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		WorkCard card = await StopAtMergeAsync(harness);
		string branch = PipelineContract.BranchName(card.Id);
		string mainTip = await harness.Clone.GetHashAsync("main");
		await harness.Clone.RunGitAsync("branch", "-D", branch);

		await RetryAsync(harness, card.Id);
		BoardCard blocked = await harness.WaitForCardAsync(card.Id, board => board.Card is { Lane: CardLane.Queue, State: CardState.Error } && board.History.StopReason?.Contains("is not in the repository", StringComparison.Ordinal) == true, "the merge to be blocked because the branch is gone", RuntimeTestTimeouts.Wait);

		IReadOnlyList<CardRound> rounds = await harness.GetHistoryAsync(card.Id);
		Assert.Equal($"{StoppedAtMerge},Merge:Blocked", RoundHistory.Describe(rounds));
		Assert.Contains($"The card's branch {branch} is not in the repository", rounds[^1].Note, StringComparison.Ordinal);
		Assert.Contains("Edit the card so it starts again at work.", rounds[^1].Note, StringComparison.Ordinal);
		Assert.Equal(RoundKind.Merge, blocked.Card.ResumeRound);
		Assert.False(await harness.Git.BranchExistsAsync(harness.Clone.Folder, branch, Token), "The blocked round must not start a branch from the target branch.");
		Assert.Equal(mainTip, await harness.Clone.GetHashAsync("main"));
		Assert.Null(await harness.Clone.ReadFileAtAsync("main", "feature.txt"));

		File.Delete(TargetBranchLock(harness));
		await using (AsyncServiceScope scope = harness.CreateScope())
		{
			CardInput edited = TestCardInput.Create(card.Name, PyxieSeedData.FeatureCategoryId, card.WorkerAgentId, reviewerAgentId: null, description: "Add the feature, again");
			ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().UpdateAsync(card.Id, edited, Token));
		}

		await RetryAsync(harness, card.Id);
		await harness.WaitForLaneAsync(card.Id, CardLane.Complete, RuntimeTestTimeouts.Wait);

		Assert.Equal($"{StoppedAtMerge},Merge:Blocked,{RestartedAndMerged}", RoundHistory.Describe(await harness.GetHistoryAsync(card.Id)));
		Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
	}
}
