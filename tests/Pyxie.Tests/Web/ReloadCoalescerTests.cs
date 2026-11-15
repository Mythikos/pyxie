using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class ReloadCoalescerTests
{
	[Fact]
	public async Task ReloadAsync_RunsTheReload()
	{
		CallProbe probe = new();
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		await coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, probe.Started);
	}

	[Fact]
	public async Task ReloadAsync_ReloadsAgainForARequestMadeAfterTheLastReloadFinished()
	{
		CallProbe probe = new();
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		await coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		Assert.Equal(3, probe.Started);
	}

	[Fact]
	public async Task ReloadAsync_AFloodOfRequestsDuringAReloadCostsOneMoreReload()
	{
		CallProbe probe = new();
		probe.Hold(1);
		probe.Hold(2);
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		Task first = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await probe.WaitForStartAsync(1);
		Task[] flood = [.. Enumerable.Range(0, 20).Select(_ => coalescer.ReloadAsync(TestContext.Current.CancellationToken))];

		probe.Release(1);
		await first;
		await probe.WaitForStartAsync(2);
		probe.Release(2);
		await Task.WhenAll(flood);

		Assert.Equal(2, probe.Started);
	}

	[Fact]
	public async Task ReloadAsync_ARequestMadeDuringAReloadWaitsForALaterReload()
	{
		CallProbe probe = new();
		probe.Hold(1);
		probe.Hold(2);
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		Task first = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await probe.WaitForStartAsync(1);
		Task second = coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		probe.Release(1);
		await first;
		await probe.WaitForStartAsync(2);

		Assert.False(second.IsCompleted, "The first reload began before the request was made, so it cannot answer it.");

		probe.Release(2);
		await second;
	}

	[Fact]
	public async Task ReloadAsync_NeverRunsTwoReloadsAtOnce()
	{
		CallProbe probe = new();
		probe.Hold(1);
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		Task first = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await probe.WaitForStartAsync(1);
		Task second = coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, probe.Started);

		probe.Release(1);
		await Task.WhenAll(first, second);

		Assert.Equal(1, probe.MostAtOnce);
	}

	[Fact]
	public async Task ReloadAsync_AFailedReloadReachesTheCallerThatRanItAndLeavesWaitingRequestsUncovered()
	{
		CallProbe probe = new();
		probe.Hold(1);
		probe.FailWith(1, new InvalidOperationException("The database is busy."));
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		Task first = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await probe.WaitForStartAsync(1);
		Task second = coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		probe.Release(1);
		InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
		await second;

		Assert.Equal("The database is busy.", failure.Message);
		Assert.Equal(2, probe.Started);
	}

	[Fact]
	public async Task ReloadAsync_AFailedReloadDoesNotCoverTheRequestsBehindIt()
	{
		CallProbe probe = new();
		probe.Hold(1);
		probe.FailWith(2, new InvalidOperationException("The database is busy."));
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		Task first = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await probe.WaitForStartAsync(1);
		Task second = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		Task third = coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		probe.Release(1);
		await first;
		await Assert.ThrowsAsync<InvalidOperationException>(() => second);
		await third;

		Assert.Equal(3, probe.Started);
	}

	[Fact]
	public async Task ReloadAsync_ARequestAfterAFailedReloadReloadsAgain()
	{
		CallProbe probe = new();
		probe.FailWith(1, new InvalidOperationException("The database is busy."));
		ReloadCoalescer coalescer = new(probe.EnterAsync);

		await Assert.ThrowsAsync<InvalidOperationException>(() => coalescer.ReloadAsync(TestContext.Current.CancellationToken));
		await coalescer.ReloadAsync(TestContext.Current.CancellationToken);

		Assert.Equal(2, probe.Started);
	}

	[Fact]
	public async Task ReloadAsync_ACancelledRequestDoesNotReloadAndDoesNotBlockLaterOnes()
	{
		CallProbe probe = new();
		probe.Hold(1);
		ReloadCoalescer coalescer = new(probe.EnterAsync);
		using CancellationTokenSource cancellation = new();

		Task first = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await probe.WaitForStartAsync(1);
		Task cancelled = coalescer.ReloadAsync(cancellation.Token);
		Task later = coalescer.ReloadAsync(TestContext.Current.CancellationToken);
		await cancellation.CancelAsync();

		probe.Release(1);
		await first;
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
		await later;

		Assert.Equal(2, probe.Started);
	}
}
