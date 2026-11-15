namespace Pyxie.Runtime.Orchestration;

/// <summary>A pulse that wakes every waiter at once. A waiter takes the current pulse's task and is released by the next <see cref="Pulse"/>; nothing is lost or consumed.</summary>
internal sealed class PipelineSignal
{
	private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Gets a task that completes at the next pulse.</summary>
	/// <returns>The task.</returns>
	public Task NextAsync()
	{
		return Volatile.Read(ref _next).Task;
	}

	/// <summary>Releases everyone waiting on the current pulse.</summary>
	public void Pulse()
	{
		Interlocked.Exchange(ref _next, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
	}
}
