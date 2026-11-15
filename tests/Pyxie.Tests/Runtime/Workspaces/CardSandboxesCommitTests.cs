using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>After a work round, Pyxie commits whatever the worker left, inside the sandbox, on the card's branch: work done on another branch is put back on it, a merge the worker left for Pyxie is committed as a merge, Pyxie's own folder never is, and a commit git refuses comes back as the reason for the next attempt's briefing.</summary>
public sealed class CardSandboxesCommitTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<IReadOnlyList<string>> CommittedFilesAsync(CardSandboxRig rig, string repository)
	{
		string files = await rig.RunGitInAsync(repository, "ls-tree", "-r", "--name-only", "HEAD");

		return files.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}

	[Fact]
	public async Task CommitAsync_CommitsEverythingTheWorkerLeftOnTheCardBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		await CardSandboxRig.WriteAsync(repository, "work.txt", "new work");
		await CardSandboxRig.WriteAsync(repository, TempGitRepository.InitialFile, "changed");

		string? problem = await rig.Cards.CommitAsync(card, "Card 1\n\nCard 1, attempt 1.", Token);

		Assert.Null(problem);
		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(repository));
		Assert.Equal(card.BaseCommit, await rig.HashInAsync(repository, "HEAD~1"));
		Assert.Equal("Card 1\n\nCard 1, attempt 1.", (await rig.RunGitInAsync(repository, "log", "-1", "--format=%B")).ReplaceLineEndings("\n"));
		Assert.Empty(await WorkingTreeStatus.ListAsync(repository));
		Assert.Contains("work.txt", await CommittedFilesAsync(rig, repository));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CommitAsync_AuthorsAndCommitsAsPyxieWhateverIdentityTheRepositoryHasConfigured(bool merging)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string cardTip = await rig.WorkRoundAsync(1, 1, "work.txt", "the card's work");
		string mainTip = await rig.MoveTargetAsync("other.txt", "main moved on");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);

		// git takes an author or committer from author.* and committer.* before user.*, which is all a worker can set in its own repository.
		await rig.RunGitInAsync(repository, "config", "author.name", "Configured author");
		await rig.RunGitInAsync(repository, "config", "author.email", "author@configured.invalid");
		await rig.RunGitInAsync(repository, "config", "committer.name", "Configured committer");
		await rig.RunGitInAsync(repository, "config", "committer.email", "committer@configured.invalid");
		if (merging)
		{
			await rig.RunGitInAsync(repository, "merge", "--quiet", "--no-commit", "--strategy=ours", "main");
			Assert.True(await rig.IsMergingAsync(repository), "The setup must leave a merge in progress.");
		}
		else
		{
			await CardSandboxRig.WriteAsync(repository, "work.txt", "left uncommitted");
		}

		Assert.Null(await rig.Cards.CommitAsync(card, "Work", Token));

		Assert.NotEqual(cardTip, await rig.HashInAsync(repository));
		Assert.Equal(merging ? mainTip : cardTip, await rig.HashInAsync(repository, merging ? "HEAD^2" : "HEAD^"));
		Assert.Equal("Pyxie|pyxie@localhost|Pyxie|pyxie@localhost", await rig.RunGitInAsync(repository, "log", "-1", "--format=%an|%ae|%cn|%ce"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CommitAsync_NeverCommitsPyxiesFolder(bool workerUnignoredIt)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		await CardSandboxRig.WriteAsync(repository, PipelineContract.BriefingFile, "briefing");
		await CardSandboxRig.WriteAsync(repository, PipelineContract.ResultFile, "{}");
		await CardSandboxRig.WriteAsync(repository, "work.txt", "new work");
		if (workerUnignoredIt)
		{
			await File.WriteAllTextAsync(Path.Combine(repository, ".git", "info", "exclude"), string.Empty, Token);
			await rig.RunGitInAsync(repository, "add", "--force", PipelineContract.ResultFile);
		}

		Assert.Null(await rig.Cards.CommitAsync(card, "Work", Token));

		IReadOnlyList<string> committed = await CommittedFilesAsync(rig, repository);
		Assert.Contains("work.txt", committed);
		Assert.DoesNotContain(committed, path => path.StartsWith(".pyxie/", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CommitAsync_WhenNothingButPyxiesFolderChanged_CommitsNothing(bool workerUnignoredIt)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		await CardSandboxRig.WriteAsync(repository, PipelineContract.ResultFile, "{}");
		if (workerUnignoredIt)
		{
			await File.WriteAllTextAsync(Path.Combine(repository, ".git", "info", "exclude"), string.Empty, Token);
		}

		Assert.Null(await rig.Cards.CommitAsync(card, "Work", Token));

		Assert.Equal(card.BaseCommit, await rig.HashInAsync(repository));
	}

	[Fact]
	public async Task CommitAsync_WhenNothingChanged_ReturnsNullAndCommitsNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);

		Assert.Null(await rig.Cards.CommitAsync(card, "Work", Token));

		Assert.Equal(card.BaseCommit, await rig.HashInAsync(CardSandboxRig.HostPath(card)));
	}

	[Theory]
	[InlineData("another branch")]
	[InlineData("detached")]
	public async Task CommitAsync_WhenTheWorkerLeftTheCardBranch_PutsItsWorkBackOnTheCardBranch(string where)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		await rig.RunGitInAsync(repository, where == "detached" ? ["checkout", "--quiet", "--detach"] : ["checkout", "--quiet", "-b", "feature"]);
		string committedElsewhere = await rig.CommitInAsync(repository, "feature.txt", "committed off the card's branch");
		await CardSandboxRig.WriteAsync(repository, "uncommitted.txt", "left uncommitted");

		Assert.Null(await rig.Cards.CommitAsync(card, "Work", Token));
		Assert.Null(await rig.Cards.TakeBackAsync(rig.Context, card, Token));

		Assert.Equal("pyxie/card-1", await rig.BranchInAsync(repository));
		Assert.Equal(committedElsewhere, await rig.HashInAsync(repository, "HEAD~1"));
		Assert.Equal(await rig.HashInAsync(repository), await rig.HubCardTipAsync(1));
		Assert.Equal("committed off the card's branch", await rig.Hub.ReadFileAtAsync("pyxie/card-1", "feature.txt"));
		Assert.Equal("left uncommitted", await rig.Hub.ReadFileAtAsync("pyxie/card-1", "uncommitted.txt"));
	}

	[Fact]
	public async Task CommitAsync_WhenTheWorkerLeftAResolvedMergeOfTheTarget_CommitsItAsAMerge()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string cardTip = await rig.WorkRoundAsync(1, 1, TempGitRepository.InitialFile, "the card's version");
		string mainTip = await rig.MoveTargetAsync(TempGitRepository.InitialFile, "main's version");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		Assert.False((await rig.TryRunGitInAsync(repository, "merge", "--no-edit", "main")).Succeeded, "The setup must leave a conflicting merge.");
		await CardSandboxRig.WriteAsync(repository, TempGitRepository.InitialFile, "both versions");

		Assert.Null(await rig.Cards.CommitAsync(card, "Merge main", Token));
		Assert.Null(await rig.Cards.TakeBackAsync(rig.Context, card, Token));

		Assert.False(await rig.IsMergingAsync(repository), "The merge is committed.");
		Assert.Equal($"{cardTip} {mainTip}", await rig.RunGitInAsync(repository, "log", "-1", "--format=%P"));
		Assert.Equal(await rig.HashInAsync(repository), await rig.HubCardTipAsync(1));
		Assert.Equal("both versions", await rig.Hub.ReadFileAtAsync("pyxie/card-1", TempGitRepository.InitialFile));
	}

	[Fact]
	public async Task CommitAsync_WhenAMergeInProgressChangesNoFiles_StillCommitsTheMerge()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string cardTip = await rig.WorkRoundAsync(1, 1, "work.txt", "the card's work");
		string mainTip = await rig.MoveTargetAsync("other.txt", "main moved on");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		await rig.RunGitInAsync(repository, "merge", "--quiet", "--no-commit", "--strategy=ours", "main");
		Assert.True(await rig.IsMergingAsync(repository), "The setup must leave a merge in progress.");

		Assert.Null(await rig.Cards.CommitAsync(card, "Merge main", Token));

		Assert.False(await rig.IsMergingAsync(repository), "The merge is committed.");
		Assert.Equal($"{cardTip} {mainTip}", await rig.RunGitInAsync(repository, "log", "-1", "--format=%P"));
	}

	[Fact]
	public async Task CommitAsync_WhenAHookRefusesTheCommit_ReturnsTheHooksReasonAndCommitsNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		string hook = Path.Combine(repository, ".git", "hooks", "pre-commit");
		await File.WriteAllTextAsync(hook, "#!/bin/sh\necho 'The work is not formatted: run the formatter.' >&2\necho 'second line' >&2\nexit 1\n", Token);
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		await CardSandboxRig.WriteAsync(repository, "work.txt", "unformatted work");

		string? problem = await rig.Cards.CommitAsync(card, "Work", Token);

		Assert.Equal("The work is not formatted: run the formatter.", problem);
		Assert.Equal(card.BaseCommit, await rig.HashInAsync(repository));
	}
}
