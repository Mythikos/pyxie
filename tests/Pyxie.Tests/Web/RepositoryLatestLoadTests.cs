using Pyxie.Runtime.Git;
using Pyxie.Web.Components.Repository.Loading;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryLatestLoadTests
{
	[Fact]
	public async Task RunAsync_AppliesTheResultOfALoadThatWasNotReplaced()
	{
		using LatestLoad load = new();
		List<int> applied = [];

		await load.RunAsync(_ => Task.FromResult(7), applied.Add, _ => Assert.Fail("The load did not fail."));

		Assert.Equal([7], applied);
	}

	[Fact]
	public async Task RunAsync_AppliesNothingFromALoadThatAnotherLoadReplacedEvenWhenItFinishesLast()
	{
		using LatestLoad load = new();
		TaskCompletionSource<string> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> applied = [];

		Task first = load.RunAsync(_ => slow.Task, applied.Add, _ => Assert.Fail("The first load did not fail."));
		await load.RunAsync(_ => Task.FromResult("new"), applied.Add, _ => Assert.Fail("The second load did not fail."));
		slow.SetResult("old");
		await first;

		Assert.Equal(["new"], applied);
	}

	[Fact]
	public async Task RunAsync_CancelsTheTokenOfTheLoadItReplaces()
	{
		using LatestLoad load = new();
		TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> applied = [];

		Task first = load.RunAsync(
			async token =>
			{
				started.SetResult();
				await Task.Delay(Timeout.Infinite, token);

				return "old";
			},
			applied.Add,
			_ => Assert.Fail("A replaced load reports nothing."));

		await started.Task;
		await load.RunAsync(_ => Task.FromResult("new"), applied.Add, _ => Assert.Fail("The second load did not fail."));
		await first;

		Assert.Equal(["new"], applied);
	}

	[Fact]
	public async Task RunAsync_ReportsGitsMessageWhenTheCurrentLoadFails()
	{
		using LatestLoad load = new();
		List<string> failures = [];

		await load.RunAsync<int>(_ => throw new GitException("git for-each-ref failed: the folder moved"), _ => Assert.Fail("The load failed."), failures.Add);

		Assert.Equal(["git for-each-ref failed: the folder moved"], failures);
	}

	[Fact]
	public async Task RunAsync_LetsAGitFailureReachTheCallerWhenThereIsNoFailureHandler()
	{
		using LatestLoad load = new();

		await Assert.ThrowsAsync<GitException>(() => load.RunAsync<int>(_ => throw new GitException("git failed"), _ => Assert.Fail("The load failed.")));
	}

	[Fact]
	public async Task RunAsync_ReportsNothingWhenAReplacedLoadFails()
	{
		using LatestLoad load = new();
		TaskCompletionSource<int> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> failures = [];

		Task first = load.RunAsync(_ => slow.Task, _ => Assert.Fail("The first load failed."), failures.Add);
		await load.RunAsync(_ => Task.FromResult(1), _ => { }, failures.Add);
		slow.SetException(new GitException("too late"));
		await first;

		Assert.Empty(failures);
	}

	[Fact]
	public async Task RunAsync_StopsTheLoadAndAppliesNothingWhenTheComponentIsDisposed()
	{
		LatestLoad load = new();
		TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<int> applied = [];

		Task running = load.RunAsync(
			async token =>
			{
				started.SetResult();
				await Task.Delay(Timeout.Infinite, token);

				return 1;
			},
			applied.Add,
			_ => Assert.Fail("A disposed load reports nothing."));

		await started.Task;
		load.Dispose();
		await running;

		Assert.Empty(applied);
	}

	[Fact]
	public async Task RunAsync_AppliesNothingAfterTheComponentWasDisposed()
	{
		LatestLoad load = new();
		load.Dispose();
		List<int> applied = [];
		bool tokenWasCancelled = false;

		await load.RunAsync(
			token =>
			{
				tokenWasCancelled = token.IsCancellationRequested;

				return Task.FromResult(1);
			},
			applied.Add,
			_ => Assert.Fail("The load did not fail."));

		Assert.True(tokenWasCancelled, "A load started after disposal should be told to stop at once.");
		Assert.Empty(applied);
	}

	[Fact]
	public async Task RunAsync_LetsACancellationThatDidNotComeFromAReplacementThrough()
	{
		using LatestLoad load = new();
		using CancellationTokenSource unrelated = new();
		await unrelated.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load.RunAsync<int>(_ => Task.FromCanceled<int>(unrelated.Token), _ => { }, _ => { }));
	}
}
