using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A worker's sandbox is kept between the card's work rounds. While the card's branch in the hub has not moved, a later work round finds what the last one left, so an interrupted or failed attempt can be carried on, minus a merge left half-done; once the branch moved (the integrator merged the target into it, say), the round starts again at its new tip. Either way the round gets the target branch as it is now.</summary>
public sealed class CardSandboxesWorkerStateTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OpenAsync_ForALaterWorkRoundWhoseCardBranchHasNotMoved_KeepsWhatTheWorkerLeft()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		string worker = rig.WorkerRepository(1);
		await CardSandboxRig.WriteAsync(worker, "work.txt", "edited and not committed");
		await CardSandboxRig.WriteAsync(worker, "untracked.txt", "half-written file");
		await CardSandboxRig.WriteAsync(worker, "staged.txt", "staged");
		await rig.RunGitInAsync(worker, "add", "staged.txt");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(Path.GetFullPath(worker), repository);
		Assert.Equal(committed, card.BaseCommit);
		Assert.False(card.BranchIsNew, "The card's branch is in the hub after its first work was taken back.");
		Assert.Equal("edited and not committed", await CardSandboxRig.ReadAsync(repository, "work.txt"));
		Assert.Equal("half-written file", await CardSandboxRig.ReadAsync(repository, "untracked.txt"));
		Assert.Equal([" M work.txt", "?? untracked.txt", "A  staged.txt"], (await WorkingTreeStatus.ListAsync(repository)).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRoundWhoseFirstWorkNeverCameBack_KeepsWhatTheWorkerLeft()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string mainTip = await rig.Hub.GetHashAsync("main");
		await using (CardSandbox first = await rig.OpenAsync(1, 1, RoundKind.Work))
		{
			await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(first), "notes.txt", "written before the round failed");
		}

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		Assert.True(card.BranchIsNew, "Nothing was taken back, so the hub still has no branch for the card.");
		Assert.Equal(mainTip, card.BaseCommit);
		Assert.Equal("written before the round failed", await CardSandboxRig.ReadAsync(CardSandboxRig.HostPath(card), "notes.txt"));
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRoundAfterTheWorkerCommittedButNothingCameBack_KeepsItsCommits()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "taken back");
		string worker = rig.WorkerRepository(1);
		string unshipped = await rig.CommitInAsync(worker, "more.txt", "committed by the agent before the round failed");
		await CardSandboxRig.WriteAsync(worker, "notes.txt", "not committed");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(committed, card.BaseCommit);
		Assert.Equal(unshipped, await rig.HashInAsync(repository));
		Assert.Equal("committed by the agent before the round failed", await CardSandboxRig.ReadAsync(repository, "more.txt"));
		Assert.Equal("not committed", await CardSandboxRig.ReadAsync(repository, "notes.txt"));
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRound_AbandonsAMergeLeftHalfDone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string mainTip = await rig.Hub.GetHashAsync("main");
		string committed = await rig.WorkRoundAsync(1, 1, TempGitRepository.InitialFile, "the card's version");
		string worker = rig.WorkerRepository(1);
		await rig.RunGitInAsync(worker, "checkout", "--quiet", "-b", "side", mainTip);
		await rig.CommitInAsync(worker, TempGitRepository.InitialFile, "the side's version");
		await rig.RunGitInAsync(worker, "checkout", "--quiet", "pyxie/card-1");
		await CardSandboxRig.WriteAsync(worker, "notes.txt", "notes the worker wrote");
		await rig.TryRunGitInAsync(worker, "merge", "--no-edit", "side");
		Assert.True(await rig.IsMergingAsync(worker), "The setup must leave a merge in progress.");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.False(await rig.IsMergingAsync(repository), "The merge in progress must be abandoned.");
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.Equal("the card's version", await CardSandboxRig.ReadAsync(repository, TempGitRepository.InitialFile));
		Assert.Equal("notes the worker wrote", await CardSandboxRig.ReadAsync(repository, "notes.txt"));
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRoundWhoseCardBranchMovedInTheHub_StartsAgainAtItsNewTip()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		string worker = rig.WorkerRepository(1);
		await CardSandboxRig.WriteAsync(worker, "work.txt", "edited and not committed");
		await CardSandboxRig.WriteAsync(worker, "untracked.txt", "half-written file");
		await rig.MoveTargetAsync("main-moved-on.txt", "main moved on");
		string merged = await rig.MergeTargetIntoCardBranchAsync(1);

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(merged, card.BaseCommit);
		Assert.Equal(merged, await rig.HashInAsync(repository));
		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(repository));
		Assert.Empty(await WorkingTreeStatus.ListAsync(repository));
		Assert.Equal("committed", await CardSandboxRig.ReadAsync(repository, "work.txt"));
		Assert.Equal("main moved on", await CardSandboxRig.ReadAsync(repository, "main-moved-on.txt"));
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRoundWhenOnlyTheTargetMoved_KeepsTheLeftoversAndBringsTheNewTarget()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		await CardSandboxRig.WriteAsync(rig.WorkerRepository(1), "notes.txt", "left by the last round");
		string mainTip = await rig.MoveTargetAsync("main-moved-on.txt", "main moved on");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.Equal("left by the last round", await CardSandboxRig.ReadAsync(repository, "notes.txt"));
		Assert.Equal(mainTip, await rig.HashInAsync(repository, "refs/heads/main"));
		Assert.Equal(mainTip, await rig.HashInAsync(repository, "refs/remotes/origin/main"));
		Assert.Equal(committed, await rig.HashInAsync(repository, "refs/remotes/origin/pyxie/card-1"));
	}

	[Fact]
	public async Task OpenAsync_ForANewCardBranchWhenTheTargetMoved_StartsAgainAtTheNewTarget()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using (CardSandbox first = await rig.OpenAsync(1, 1, RoundKind.Work))
		{
			await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(first), "notes.txt", "built on the old target");
		}

		string mainTip = await rig.MoveTargetAsync("main-moved-on.txt", "main moved on");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.True(card.BranchIsNew, "Nothing was taken back, so the hub still has no branch for the card.");
		Assert.Equal(mainTip, card.BaseCommit);
		Assert.Equal(mainTip, await rig.HashInAsync(repository));
		Assert.Empty(await WorkingTreeStatus.ListAsync(repository));
	}

	[Theory]
	[InlineData("another branch")]
	[InlineData("detached")]
	public async Task OpenAsync_ForALaterWorkRoundWhenTheWorkerLeftTheCardBranch_PutsItBackAtTheCardBranchsTip(string where)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		string worker = rig.WorkerRepository(1);
		await rig.RunGitInAsync(worker, where == "detached" ? ["checkout", "--quiet", "--detach"] : ["checkout", "--quiet", "-b", "someone-elses-branch"]);
		await rig.CommitInAsync(worker, "other.txt", "work off the card's branch");
		await CardSandboxRig.WriteAsync(worker, "sentinel.txt", "left by the last round");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(repository));
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.False(File.Exists(Path.Combine(repository, "other.txt")), "Work off the card's branch is not on it.");
		Assert.False(File.Exists(Path.Combine(repository, "sentinel.txt")), "A worker that left the card's branch starts again at its tip.");
	}

	[Fact]
	public async Task OpenAsync_WhenTheWorkersRepositoryIsGone_MakesItAgainFromTheCardBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		LocalSandboxes.DeleteFolder(rig.WorkerRepository(1));

		await using CardSandbox card = await rig.OpenAsync(1, 3, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(committed, card.BaseCommit);
		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(repository));
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.Equal("committed", await CardSandboxRig.ReadAsync(repository, "work.txt"));
		Assert.Contains("/.pyxie/", await File.ReadAllLinesAsync(Path.Combine(repository, ".git", "info", "exclude"), Token));
	}

	[Fact]
	public async Task OpenAsync_WhenTheWorkersRepositorySetUpWasCutShort_StillGivesItPyxiesIdentityAndExclude()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		string worker = rig.WorkerRepository(1);
		LocalSandboxes.DeleteFolder(worker);
		Directory.CreateDirectory(worker);
		await rig.RunGitInAsync(worker, "init", "--quiet");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.Equal("Pyxie", (await rig.TryRunGitInAsync(repository, "config", "--local", "user.name")).StandardOutput.Trim());
		string exclude = Path.Combine(repository, ".git", "info", "exclude");
		Assert.Contains("/.pyxie/", File.Exists(exclude) ? await File.ReadAllLinesAsync(exclude, Token) : [], StringComparer.Ordinal);
	}

	[Fact]
	public async Task OpenAsync_ForALaterWorkRound_RemovesAnIndexLockLeftByAKilledGitCommand()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		string worker = rig.WorkerRepository(1);
		await CardSandboxRig.WriteAsync(worker, "sentinel.txt", "left by the interrupted round");
		string indexLock = Path.Combine(worker, ".git", "index.lock");
		await File.WriteAllTextAsync(indexLock, string.Empty, Token);

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		string? problem = await rig.Cards.CommitAsync(card, "Carry on", Token);

		Assert.False(File.Exists(indexLock), "The stale index.lock must be removed.");
		Assert.Null(problem);
		Assert.Equal(committed, await rig.HashInAsync(CardSandboxRig.HostPath(card), "HEAD~1"));
		Assert.Equal("left by the interrupted round", await CardSandboxRig.ReadAsync(CardSandboxRig.HostPath(card), "sentinel.txt"));
	}

	[Fact]
	public async Task OpenAsync_EmptiesTheSandboxsInputAndOutputFoldersFirst()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await (await rig.OpenAsync(1, 1, RoundKind.Work)).DisposeAsync();

		string folder = rig.SandboxFolder(1, SandboxRole.Worker);
		await CardSandboxRig.WriteAsync(folder, "in/stale.txt", "left from the last round");
		await CardSandboxRig.WriteAsync(folder, "out/result.bundle", "left from the last round");
		await CardSandboxRig.WriteAsync(folder, "out/nested/report.txt", "left from the last round");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		Assert.Equal(["card.bundle"], Directory.EnumerateFileSystemEntries(card.Sandbox.InputFolder).Select(Path.GetFileName));
		Assert.Empty(Directory.EnumerateFileSystemEntries(card.Sandbox.OutputFolder));
	}
}
