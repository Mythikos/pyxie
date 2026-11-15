using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

public sealed class RepositoryLockTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	// A semaphore that is taken makes the next wait pending at once, so "has not completed" is checked right after asking, with no sleep.
	[Fact]
	public async Task AcquireAsync_WhileAnotherHolderHasTheLock_WaitsUntilItIsReleased()
	{
		using RepositoryLock repositoryLock = new();
		IDisposable first = await repositoryLock.AcquireAsync(Token);

		Task<IDisposable> second = repositoryLock.AcquireAsync(Token);
		Assert.False(second.IsCompleted, "A second holder must wait while the first one holds the lock.");

		first.Dispose();
		using IDisposable acquired = await second;

		Assert.True(second.IsCompletedSuccessfully, "The waiting holder gets the lock once it is released.");
	}

	[Fact]
	public async Task Release_WhenDisposedTwice_ReleasesOnlyOnce()
	{
		using RepositoryLock repositoryLock = new();
		IDisposable first = await repositoryLock.AcquireAsync(Token);
		first.Dispose();
		using IDisposable second = await repositoryLock.AcquireAsync(Token);

		first.Dispose();
		Task<IDisposable> third = repositoryLock.AcquireAsync(Token);

		Assert.False(third.IsCompleted, "Disposing a release twice must not hand the lock to a third holder while the second one still holds it.");
		second.Dispose();
		using IDisposable acquired = await third;
	}

	[Fact]
	public async Task AcquireAsync_WhenCancelledWhileWaiting_ThrowsWithoutTakingTheLock()
	{
		using RepositoryLock repositoryLock = new();
		IDisposable held = await repositoryLock.AcquireAsync(Token);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

		Task<IDisposable> waiting = repositoryLock.AcquireAsync(cancel.Token);
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
		held.Dispose();
		using IDisposable acquired = await repositoryLock.AcquireAsync(Token);
	}
}
