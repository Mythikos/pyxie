using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>When the pipeline starts, Pyxie removes what cards that no longer need it left: a card is found by any of its sandboxes, its branch in the hub, or its host folder, and is removed unless its work is unfinished. The orchestrator logs the cards it reports, so it reports exactly the cards it removed, in order. Gate and review sandboxes still there are removed for every card, because none runs while the pipeline starts.</summary>
public sealed class CardSandboxesRemoveAbandonedTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task StartSandboxAsync(CardSandboxRig rig, int cardId, SandboxRole role)
	{
		string folder = rig.SandboxFolder(cardId, role);
		SandboxSpec spec = new(SandboxNames.Container(rig.ProjectKey, cardId, role), LocalSandboxes.Image, rig.ProjectKey, cardId, role, Path.Combine(folder, "in"), Path.Combine(folder, "out"));

		// The sandbox is left running, the way a round cut off by Pyxie stopping leaves it.
		await rig.Sandboxes.StartAsync(spec, Token);
	}

	private static string EngineFolder(CardSandboxRig rig, int cardId, SandboxRole role)
	{
		return Path.GetDirectoryName(rig.Sandboxes.WorkFolder(SandboxNames.Container(rig.ProjectKey, cardId, role))) ?? throw new InvalidOperationException("A sandbox folder has a parent.");
	}

	[Fact]
	public async Task RemoveAbandonedAsync_RemovesEveryCardNotKeptAndReportsThemInOrder()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(3, 1, "three.txt", "card 3");
		await rig.WorkRoundAsync(1, 1, "one.txt", "card 1");
		string kept = await rig.WorkRoundAsync(2, 1, "two.txt", "card 2");

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int> { 2 }, Token);

		Assert.Equal([1, 3], removed);
		foreach (int cardId in new[] { 1, 3 })
		{
			Assert.Null(await rig.HubCardTipAsync(cardId));
			Assert.False(Directory.Exists(EngineFolder(rig, cardId, SandboxRole.Worker)), $"Card {cardId}'s sandbox must be gone.");
			Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, cardId)), $"Card {cardId}'s host folders must be gone.");
		}

		Assert.Equal(kept, await rig.HubCardTipAsync(2));
		Assert.True(Directory.Exists(rig.WorkerRepository(2)), "The kept card's sandbox stays.");
		Assert.True(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, 2)), "The kept card's host folders stay.");
	}

	[Fact]
	public async Task RemoveAbandonedAsync_RemovesACardThatOnlyHasABranchInTheHub()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("pyxie/card-5");
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int>(), Token);

		Assert.Equal([5], removed);
		Assert.Null(await rig.HubCardTipAsync(5));
	}

	[Fact]
	public async Task RemoveAbandonedAsync_RemovesACardThatOnlyHasASandbox()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await StartSandboxAsync(rig, 6, SandboxRole.Worker);
		Directory.Delete(WorkspaceLayout.SandboxesFolder(rig.ProjectFolder), recursive: true);

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int>(), Token);

		Assert.Equal([6], removed);
		Assert.Empty(await rig.Sandboxes.ListCardsAsync(rig.ProjectKey, Token));
	}

	[Fact]
	public async Task RemoveAbandonedAsync_RemovesACardThatOnlyHasAHostFolder()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await CardSandboxRig.WriteAsync(rig.SandboxFolder(7, SandboxRole.Worker), "in/card.bundle", "left behind");

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int>(), Token);

		Assert.Equal([7], removed);
		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, 7)), "The card's host folder must be gone.");
	}

	[Fact]
	public async Task RemoveAbandonedAsync_RemovesGateReviewAndResolveSandboxesLeftByKeptCardsAndKeepsTheirWorkers()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string kept = await rig.WorkRoundAsync(2, 1, "two.txt", "card 2");
		await StartSandboxAsync(rig, 2, SandboxRole.Gates);
		await StartSandboxAsync(rig, 2, SandboxRole.Review);
		await StartSandboxAsync(rig, 2, SandboxRole.Resolve);

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int> { 2 }, Token);

		Assert.Empty(removed);
		Assert.False(Directory.Exists(EngineFolder(rig, 2, SandboxRole.Gates)), "A gate sandbox left by a cut-off round must be gone.");
		Assert.False(Directory.Exists(EngineFolder(rig, 2, SandboxRole.Review)), "A review sandbox left by a cut-off round must be gone.");
		Assert.False(Directory.Exists(EngineFolder(rig, 2, SandboxRole.Resolve)), "A resolve sandbox left by a cut-off round must be gone.");
		Assert.True(Directory.Exists(rig.WorkerRepository(2)), "The kept card's worker sandbox stays.");
		Assert.Equal(kept, await rig.HubCardTipAsync(2));
	}

	[Fact]
	public async Task RemoveAbandonedAsync_LeavesEveryBranchThatIsNotACardBranch()
	{
		string[] branches = ["feature", "pyxie/other", "pyxie/card-abc", "pyxie/card-01", "pyxie/card-0", "pyxie/card-1-extra", "release/pyxie/card-4"];
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		foreach (string name in branches)
		{
			await source.CreateBranchAsync(name);
		}

		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int>(), Token);

		Assert.Empty(removed);
		foreach (string name in branches.Append("main"))
		{
			Assert.True(await rig.Git.BranchExistsAsync(rig.Hub.Folder, name, Token), $"The branch {name} is not a card branch and must stay.");
		}
	}

	[Fact]
	public async Task RemoveAbandonedAsync_LeavesFoldersThatAreNotCardFolders()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string sandboxes = WorkspaceLayout.SandboxesFolder(rig.ProjectFolder);
		foreach (string name in new[] { "notes", "card-x", "card--1", "card-" })
		{
			Directory.CreateDirectory(Path.Combine(sandboxes, name));
		}

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int>(), Token);

		Assert.Empty(removed);
		foreach (string name in new[] { "notes", "card-x", "card--1", "card-" })
		{
			Assert.True(Directory.Exists(Path.Combine(sandboxes, name)), $"The folder {name} is not a card's and must stay.");
		}
	}

	[Fact]
	public async Task RemoveAbandonedAsync_WhenNothingIsAbandoned_ChangesNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string kept = await rig.WorkRoundAsync(1, 1, "work.txt", "card 1");

		IReadOnlyList<int> removed = await rig.Cards.RemoveAbandonedAsync(rig.ProjectFolder, rig.Repository, new HashSet<int> { 1, 2, 3 }, Token);

		Assert.Empty(removed);
		Assert.Equal(kept, await rig.HubCardTipAsync(1));
		Assert.True(Directory.Exists(rig.WorkerRepository(1)), "The kept card's sandbox stays.");
		Assert.True(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, 1)), "The kept card's host folders stay.");
	}
}
