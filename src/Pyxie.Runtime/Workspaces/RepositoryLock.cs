namespace Pyxie.Runtime.Workspaces;

/// <summary>Serializes the git operations that move the hub's branches: taking a worker's commits back, deleting a card's branch, and integrating a card into the target branch. Work inside a card's sandbox does not need it. One Pyxie process owns a repository, so a process-wide lock is enough.</summary>
public sealed class RepositoryLock : IDisposable
{
	private readonly SemaphoreSlim _semaphore = new(1, 1);

	/// <summary>Waits for the lock.</summary>
	/// <param name="cancellationToken">A token to stop waiting.</param>
	/// <returns>A handle that releases the lock when disposed.</returns>
	public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
	{
		await _semaphore.WaitAsync(cancellationToken);

		return new RepositoryLockRelease(_semaphore);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_semaphore.Dispose();
	}
}
