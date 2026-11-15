using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>Keeps a test from hanging for good when the code under test waits for a process that never ends: the wait fails with a message instead.</summary>
public static class TaskBounds
{
	private static readonly TimeSpan s_limit = TimeSpan.FromSeconds(90);

	/// <summary>Awaits a task for at most ninety seconds.</summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="task">The task.</param>
	/// <returns>The task's result. The test fails when the task has not finished by then.</returns>
	public static async Task<T> WithinLimitAsync<T>(this Task<T> task)
	{
		try
		{
			return await task.WaitAsync(s_limit, TestContext.Current.CancellationToken);
		}
		catch (TimeoutException)
		{
			Assert.Fail($"The operation did not finish within {s_limit.TotalSeconds:0} seconds.");

			throw;
		}
	}

	/// <summary>Awaits a task for at most ninety seconds.</summary>
	/// <param name="task">The task.</param>
	/// <returns>A task that completes with <paramref name="task"/>. The test fails when it has not finished by then.</returns>
	public static async Task WithinLimitAsync(this Task task)
	{
		try
		{
			await task.WaitAsync(s_limit, TestContext.Current.CancellationToken);
		}
		catch (TimeoutException)
		{
			Assert.Fail($"The operation did not finish within {s_limit.TotalSeconds:0} seconds.");
		}
	}
}
