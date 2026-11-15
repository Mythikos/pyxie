using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Removing a card's leftovers takes its sandboxes and their volumes, its host folders, and its branch in the hub, whatever work is in them, and nothing of any other card or project.</summary>
public sealed class CardSandboxesRemoveTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string EngineFolder(CardSandboxRig rig, string projectKey, int cardId, SandboxRole role)
	{
		return Path.GetDirectoryName(rig.Sandboxes.WorkFolder(SandboxNames.Container(projectKey, cardId, role))) ?? throw new InvalidOperationException("A sandbox folder has a parent.");
	}

	[Fact]
	public async Task RemoveAsync_RemovesTheCardsSandboxesHostFoldersAndBranchEvenWithUnfinishedWork()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "work.txt", "not merged");
		await CardSandboxRig.WriteAsync(rig.WorkerRepository(1), "untracked.txt", "not committed");

		await rig.Cards.RemoveAsync(rig.ProjectFolder, rig.Repository, 1, Token);

		Assert.False(Directory.Exists(EngineFolder(rig, rig.ProjectKey, 1, SandboxRole.Worker)), "The card's worker sandbox, and its volumes, must be gone.");
		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, 1)), "The card's host folders must be gone.");
		Assert.Null(await rig.HubCardTipAsync(1));
		Assert.Empty(await rig.Sandboxes.ListCardsAsync(rig.ProjectKey, Token));
	}

	[Fact]
	public async Task RemoveAsync_LeavesOtherCardsAlone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "one.txt", "card 1");
		string cardTwo = await rig.WorkRoundAsync(2, 1, "two.txt", "card 2");

		await rig.Cards.RemoveAsync(rig.ProjectFolder, rig.Repository, 1, Token);

		Assert.True(Directory.Exists(rig.WorkerRepository(2)), "Card 2's sandbox stays.");
		Assert.True(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.ProjectFolder, 2)), "Card 2's host folders stay.");
		Assert.Equal(cardTwo, await rig.HubCardTipAsync(2));
	}

	[Fact]
	public async Task RemoveAsync_LeavesAnotherProjectsSandboxForTheSameCardAlone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		const string OtherProject = "0123456789ab";
		string otherFolder = Path.Combine(Path.GetDirectoryName(rig.ProjectFolder) ?? rig.ProjectFolder, "other-project", "card-1", "work");
		SandboxSpec other = new(SandboxNames.Container(OtherProject, 1, SandboxRole.Worker), LocalSandboxes.Image, OtherProject, 1, SandboxRole.Worker, Path.Combine(otherFolder, "in"), Path.Combine(otherFolder, "out"));
		await (await rig.Sandboxes.StartAsync(other, Token)).DisposeAsync();
		await rig.WorkRoundAsync(1, 1, "work.txt", "card 1");

		await rig.Cards.RemoveAsync(rig.ProjectFolder, rig.Repository, 1, Token);

		Assert.True(Directory.Exists(EngineFolder(rig, OtherProject, 1, SandboxRole.Worker)), "Another project's sandbox for a card with the same number is not this project's to remove.");
	}

	[Fact]
	public async Task RemoveAsync_ForACardWithNothingLeft_DoesNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);

		await rig.Cards.RemoveAsync(rig.ProjectFolder, rig.Repository, 99, Token);

		Assert.Equal("refs/heads/main", await rig.Hub.RunGitAsync("for-each-ref", "--format=%(refname)", "refs/heads"));
	}

	// The sandboxes go before the branch, which is deleted under the lock; so a gone sandbox while the lock is held is a point the removal reaches on its own: no fixed wait.
	[Fact]
	public async Task RemoveAsync_DeletesTheBranchOnlyUnderTheRepositoryLock()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "card 1");
		string sandbox = EngineFolder(rig, rig.ProjectKey, 1, SandboxRole.Worker);
		Task remove;

		using (await rig.RepositoryLock.AcquireAsync(Token))
		{
			remove = rig.Cards.RemoveAsync(rig.ProjectFolder, rig.Repository, 1, Token);
			await CardSandboxRig.WaitUntilAsync(() => Task.FromResult(!Directory.Exists(sandbox)), "the card's sandbox to be removed");

			Assert.False(remove.IsCompleted, "Removing the card must wait for the repository lock before it deletes the branch.");
			Assert.Equal(committed, await rig.HubCardTipAsync(1));
		}

		await remove;

		Assert.Null(await rig.HubCardTipAsync(1));
	}
}
