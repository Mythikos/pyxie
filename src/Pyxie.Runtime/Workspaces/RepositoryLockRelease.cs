namespace Pyxie.Runtime.Workspaces;

/// <summary>Releases a <see cref="RepositoryLock"/> once, however many times it is disposed.</summary>
internal sealed class RepositoryLockRelease : IDisposable
{
	private SemaphoreSlim? _semaphore;

	/// <summary>Initializes a new instance of the <see cref="RepositoryLockRelease"/> class.</summary>
	/// <param name="semaphore">The semaphore held by the caller.</param>
	public RepositoryLockRelease(SemaphoreSlim semaphore)
	{
		_semaphore = semaphore;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Interlocked.Exchange(ref _semaphore, null)?.Release();
	}
}
