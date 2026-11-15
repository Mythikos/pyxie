using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A git command that changes the repository is not cancelled once it started: git killed part-way leaves lock files and half-written refs behind in the hub. Each test holds git inside the command with a <c>reference-transaction</c> hook (<see cref="GitBlocker"/>), which git runs while it holds the lock on the ref it is writing, cancels the caller's token, checks that git is still running, and then lets it finish.</summary>
public sealed class GitClientChangeCancellationTests
{
	private const string Incoming = "refs/pyxie/incoming/card-1";
	private const string Hook = "reference-transaction";

	private static readonly TimeSpan s_stillRunningCheck = TimeSpan.FromMilliseconds(500);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task FetchBundleAsync_WhenCancelledWhileGitWritesTheRef_StillWritesIt()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		await using TempGitRepository sandbox = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(sandbox);
		string bundle = await WriteHandOutAsync(sandbox, scratch);
		string tip = await sandbox.GetHashAsync("main");
		using GitBlocker blocker = new(scratch.Folder);
		await HoldEveryRefUpdateAsync(hub, blocker);
		using CancellationTokenSource cancellation = new();

		Task fetch = hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/main:{Incoming}", cancellation.Token);
		await blocker.WaitUntilBlockedAsync();
		await cancellation.CancelAsync();
		await AssertStillRunningAsync(fetch, "git fetch");
		blocker.Release();

		await fetch;
		Assert.Equal(tip, await hub.Repository.GetHashAsync(Incoming));
		AssertNoLockLeft(hub);
	}

	[Fact]
	public async Task TryMoveBranchAsync_WhenCancelledWhileGitMovesTheBranch_StillMovesIt()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		string head = await hub.Repository.GetHashAsync("main");
		string merge = await hub.Repository.Git.CommitTreeAsync(hub.Folder, await hub.Repository.GetHashAsync("main^{tree}"), [head], "Merge written without a checkout", Token);
		using GitBlocker blocker = new(scratch.Folder);
		await HoldEveryRefUpdateAsync(hub, blocker);
		using CancellationTokenSource cancellation = new();

		Task<bool> move = hub.Repository.Git.TryMoveBranchAsync(hub.Folder, "main", merge, head, cancellation.Token);
		await blocker.WaitUntilBlockedAsync();
		await cancellation.CancelAsync();
		await AssertStillRunningAsync(move, "git update-ref");
		blocker.Release();

		Assert.True(await move, "The branch still pointed at the expected commit, so the move must succeed although the caller cancelled it.");
		Assert.Equal(merge, await hub.Repository.GetHashAsync("main"));
		AssertNoLockLeft(hub);
	}

	[Fact]
	public async Task FetchBundleAsync_WhenTheTokenWasCancelledBeforeItStarted_DoesNotStartGit()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		await using TempGitRepository sandbox = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(sandbox);
		string bundle = await WriteHandOutAsync(sandbox, scratch);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/main:{Incoming}", cancellation.Token));

		Assert.Equal(refsBefore, await hub.ListRefsAsync());
		AssertNoLockLeft(hub);
	}

	// A bundle of one new commit on main, built on the commit the hub already has, as a worker hands its work out.
	private static async Task<string> WriteHandOutAsync(TempGitRepository sandbox, ScratchFolder scratch)
	{
		string baseCommit = await sandbox.GetHashAsync("main");
		await sandbox.CommitFileAsync("work.txt", "the worker's change");
		string bundle = scratch.Combine("hand-out.bundle");
		await sandbox.Git.CreateBundleAsync(sandbox.Folder, bundle, ["refs/heads/main", $"^{baseCommit}"], Token);

		return bundle;
	}

	// The hub names its own hooks folder, as Pyxie's hub does, so a hooks folder set in the machine's git config cannot replace the blocking hook. Installed last: every ref update in the hub blocks on it from now on.
	private static async Task HoldEveryRefUpdateAsync(BareRepository hub, GitBlocker blocker)
	{
		string hooks = Directory.CreateDirectory(Path.Combine(hub.Repository.GitFolder, "hooks")).FullName;
		await hub.Repository.RunGitAsync("config", "core.hooksPath", hooks);
		await blocker.InstallHookAsync(hub.Repository.GitFolder, Hook, Token);
	}

	private static async Task AssertStillRunningAsync(Task task, string command)
	{
		Task finished = await Task.WhenAny(task, Task.Delay(s_stillRunningCheck, Token));
		Assert.False(ReferenceEquals(finished, task), $"{command} ended after the caller cancelled it, although git was still working: a command that changes the repository must run to the end.");
	}

	private static void AssertNoLockLeft(BareRepository hub)
	{
		Assert.Empty(Directory.EnumerateFiles(hub.Folder, "*.lock", SearchOption.AllDirectories));
	}
}
