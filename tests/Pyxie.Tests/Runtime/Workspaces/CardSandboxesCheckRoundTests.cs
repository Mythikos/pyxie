using Pyxie.Data.Enums;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Gate and review rounds check the card's last commit and nothing else, in a sandbox of their own: what the worker left uncommitted never reaches them, and they never touch it.</summary>
public sealed class CardSandboxesCheckRoundTests
{
	[Theory]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	public async Task OpenAsync_ForARoundThatChecksWork_StartsFromTheCardsLastCommitWithNothingElse(RoundKind kind)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		string worker = rig.WorkerRepository(1);
		await CardSandboxRig.WriteAsync(worker, "work.txt", "edited by the worker");
		await CardSandboxRig.WriteAsync(worker, "untracked.txt", "untracked");
		await CardSandboxRig.WriteAsync(worker, "staged.txt", "staged");
		await rig.RunGitInAsync(worker, "add", "staged.txt");

		await using CardSandbox card = await rig.OpenAsync(1, 2, kind);

		string repository = CardSandboxRig.HostPath(card);
		Assert.NotEqual(Path.GetFullPath(worker), repository);
		Assert.Equal(committed, card.BaseCommit);
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.Empty(await WorkingTreeStatus.ListAsync(repository));
		Assert.Equal("committed", await CardSandboxRig.ReadAsync(repository, "work.txt"));
		Assert.Equal([" M work.txt", "?? untracked.txt", "A  staged.txt"], (await WorkingTreeStatus.ListAsync(worker)).Order(StringComparer.Ordinal));
	}
}
