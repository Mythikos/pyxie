using Pyxie.Data.Enums;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A worker's new commits come back into the hub as a bundle: only a plain file is read, it is verified and fetched into a ref of Pyxie's own, and the card's branch moves to it, by compare-and-swap under the repository lock, only when it builds on the commit the round started from. Whatever happens, the fetched ref and the copied bundle are gone afterwards.</summary>
public sealed class CardSandboxesTakeBackTests
{
	private const string ReceivedFile = "received.bundle";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task AssertNothingLeftAsync(CardSandboxRig rig, CardSandbox card)
	{
		Assert.Empty(await rig.PyxieRefsAsync());
		Assert.False(File.Exists(Path.Combine(card.Folder, ReceivedFile)), "The bundle copied out of the sandbox must be deleted.");
	}

	private static CardSandbox Tampered(CardSandbox card, Func<string, Task> tamper)
	{
		return new CardSandbox(new TamperingSandbox(card.Sandbox, tamper), card.RepositoryPath, card.Branch, card.BaseCommit, card.TargetCommit, card.BranchIsNew, card.Folder);
	}

	private static bool CanCreateSymbolicLinks(string folder)
	{
		string link = Path.Combine(folder, "link-probe");
		try
		{
			File.CreateSymbolicLink(link, Path.Combine(folder, "missing-target"));
			File.Delete(link);

			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	[Fact]
	public async Task TakeBackAsync_ForANewCardBranchWithNoCommits_CreatesItAtTheBase()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string mainTip = await rig.Hub.GetHashAsync("main");
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		Assert.Null(await rig.Cards.CommitAsync(card, "Nothing", Token));

		string? problem = await rig.Cards.TakeBackAsync(rig.Context, card, Token);

		Assert.Null(problem);
		Assert.Equal(mainTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackAsync_ForACardBranchWithNoNewCommits_LeavesItWhereItIs()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);

		Assert.Null(await rig.Cards.TakeBackAsync(rig.Context, card, Token));

		Assert.Equal(committed, await rig.HubCardTipAsync(1));
	}

	[Fact]
	public async Task TakeBackAsync_BringsTheWorkersCommitsBackAndMovesTheCardBranchToThem()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox first = await rig.OpenAsync(1, 1, RoundKind.Work);
		await rig.CommitInAsync(CardSandboxRig.HostPath(first), "a.txt", "first commit");
		string firstHead = await rig.CommitInAsync(CardSandboxRig.HostPath(first), "b.txt", "second commit");

		Assert.Null(await rig.Cards.TakeBackAsync(rig.Context, first, Token));

		Assert.Equal(firstHead, await rig.HubCardTipAsync(1));
		Assert.Equal("first commit", await rig.Hub.ReadFileAtAsync("pyxie/card-1", "a.txt"));
		Assert.Equal("second commit", await rig.Hub.ReadFileAtAsync("pyxie/card-1", "b.txt"));
		await AssertNothingLeftAsync(rig, first);

		await using CardSandbox second = await rig.OpenAsync(1, 2, RoundKind.Work);
		string secondHead = await rig.CommitInAsync(CardSandboxRig.HostPath(second), "c.txt", "a later round's commit");

		Assert.Null(await rig.Cards.TakeBackAsync(rig.Context, second, Token));

		Assert.Equal(secondHead, await rig.HubCardTipAsync(1));
		Assert.Equal(firstHead, await rig.Hub.GetHashAsync("pyxie/card-1~1"));
		await AssertNothingLeftAsync(rig, second);
	}

	[Fact]
	public async Task TakeBackAsync_WhenTheWorkDoesNotBuildOnTheCardBranch_RefusesItWithAMessage()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "taken back");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		string repository = CardSandboxRig.HostPath(card);
		await CardSandboxRig.WriteAsync(repository, "work.txt", "history rewritten");
		await rig.RunGitInAsync(repository, "commit", "--quiet", "--all", "--amend", "--no-edit");

		string? problem = await rig.Cards.TakeBackAsync(rig.Context, card, Token);

		Assert.Equal($"The work that came back does not build on pyxie/card-1's last commit, {committed[..12]}.", problem);
		Assert.Equal(committed, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackAsync_WhenTheWorkerMovedTheCardBranchBack_RefusesItWithAMessage()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "taken back");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		await rig.RunGitInAsync(CardSandboxRig.HostPath(card), "reset", "--quiet", "--hard", "HEAD~1");

		string? problem = await rig.Cards.TakeBackAsync(rig.Context, card, Token);

		Assert.True(problem is not null && problem.StartsWith("The work could not be handed out of the sandbox:", StringComparison.Ordinal), $"Unexpected answer: {problem}");
		Assert.Equal(committed, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Theory]
	[InlineData("a folder")]
	[InlineData("nothing")]
	[InlineData("a symbolic link")]
	public async Task TakeBackAsync_WhenTheSandboxHandsOutSomethingOtherThanABundleFile_ThrowsASandboxExceptionAndReadsNothing(string handedOut)
	{
		await using ScratchFolder elsewhere = ScratchFolder.Create("pyxie-elsewhere");
		Assert.SkipWhen(handedOut == "a symbolic link" && !CanCreateSymbolicLinks(elsewhere.Folder), "This machine does not let the test create symbolic links.");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "taken back");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		await rig.CommitInAsync(CardSandboxRig.HostPath(card), "more.txt", "more work");
		CardSandbox tampered = Tampered(card, output =>
		{
			string bundle = Path.Combine(output, "result.bundle");
			string moved = Path.Combine(elsewhere.Folder, "result.bundle");
			File.Move(bundle, moved);
			if (handedOut == "a folder")
			{
				Directory.CreateDirectory(bundle);
			}
			else if (handedOut == "a symbolic link")
			{
				File.CreateSymbolicLink(bundle, moved);
			}

			return Task.CompletedTask;
		});

		SandboxException failure = await Assert.ThrowsAsync<SandboxException>(() => rig.Cards.TakeBackAsync(rig.Context, tampered, Token));

		Assert.Equal("The sandbox handed out something other than a bundle file.", failure.Message);
		Assert.Equal(committed, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackAsync_WhenTheBundleIsNotABundle_ThrowsTheVerifyFailureAndLeavesNothingBehind()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "taken back");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		await rig.CommitInAsync(CardSandboxRig.HostPath(card), "more.txt", "more work");
		CardSandbox tampered = Tampered(card, output => File.WriteAllTextAsync(Path.Combine(output, "result.bundle"), "not a bundle", Token));

		GitException failure = await Assert.ThrowsAsync<GitException>(() => rig.Cards.TakeBackAsync(rig.Context, tampered, Token));

		Assert.StartsWith("git bundle failed", failure.Message, StringComparison.Ordinal);
		Assert.Equal(committed, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackAsync_WhenTheCardBranchMovedInTheHubMeanwhile_RefusesTheWork()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "work.txt", "taken back");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Work);
		await rig.CommitInAsync(CardSandboxRig.HostPath(card), "more.txt", "more work");
		await rig.MoveTargetAsync("main-moved-on.txt", "main moved on");
		string merged = await rig.MergeTargetIntoCardBranchAsync(1);

		string? problem = await rig.Cards.TakeBackAsync(rig.Context, card, Token);

		Assert.Equal("pyxie/card-1 moved in the repository while the round ran.", problem);
		Assert.Equal(merged, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackAsync_WhenTheCardBranchWasCreatedInTheHubMeanwhile_RefusesTheWork()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string mainTip = await rig.Hub.GetHashAsync("main");
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		await rig.CommitInAsync(CardSandboxRig.HostPath(card), "work.txt", "new work");
		await rig.Hub.RunGitAsync("branch", "pyxie/card-1", mainTip);

		string? problem = await rig.Cards.TakeBackAsync(rig.Context, card, Token);

		Assert.Equal("pyxie/card-1 moved in the repository while the round ran.", problem);
		Assert.Equal(mainTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	// The incoming ref appears before the move waits for the lock, so seeing it while the lock is held is a point the take-back reaches on its own: no fixed wait.
	[Fact]
	public async Task TakeBackAsync_MovesTheCardBranchOnlyUnderTheRepositoryLock()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);
		string head = await rig.CommitInAsync(CardSandboxRig.HostPath(card), "work.txt", "new work");
		Task<string?> takeBack;

		using (await rig.RepositoryLock.AcquireAsync(Token))
		{
			takeBack = rig.Cards.TakeBackAsync(rig.Context, card, Token);
			await CardSandboxRig.WaitUntilAsync(async () => (await rig.PyxieRefsAsync()).Count > 0, "the work to be fetched into the hub");

			Assert.False(takeBack.IsCompleted, "Taking the work back must wait for the repository lock.");
			Assert.Null(await rig.HubCardTipAsync(1));
		}

		Assert.Null(await takeBack);
		Assert.Equal(head, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}
}
