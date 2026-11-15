using Pyxie.Runtime.Orchestration;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Reads <see cref="PipelineController.RunningRounds"/> every time the controller raises <see cref="PipelineController.StateChanged"/> and remembers what it read, so a test sees each count the pipeline went through, which polling would miss, and waits for one without sleeping. A count that repeats is listed once. A handler runs on the thread that changed the count and reads the count under the recorder's lock, so the last count listed is always the current one; a count between two quick changes made by different threads can be skipped.</summary>
public sealed class RunningRoundsRecorder : IDisposable
{
	private readonly PipelineController _controller;
	private readonly Lock _gate = new();
	private readonly List<int> _counts = [];
	private readonly List<(Func<IReadOnlyList<int>, bool> Condition, TaskCompletionSource Met)> _waits = [];

	/// <summary>Initializes a new instance of the <see cref="RunningRoundsRecorder"/> class and starts listening, with the count as it is now as the first one.</summary>
	/// <param name="controller">The controller whose count to record.</param>
	public RunningRoundsRecorder(PipelineController controller)
	{
		_controller = controller;
		_counts.Add(controller.RunningRounds);
		_controller.StateChanged += OnStateChanged;
	}

	/// <summary>Gets the counts seen so far, oldest first, a count that repeated listed once.</summary>
	public IReadOnlyList<int> Counts
	{
		get
		{
			lock (_gate)
			{
				return [.. _counts];
			}
		}
	}

	/// <summary>Waits until the counts seen so far satisfy a condition. A condition that already holds completes at once.</summary>
	/// <param name="condition">The condition over <see cref="Counts"/>.</param>
	/// <returns>A task that completes when the condition holds.</returns>
	public Task WhenAsync(Func<IReadOnlyList<int>, bool> condition)
	{
		lock (_gate)
		{
			if (condition(_counts))
			{
				return Task.CompletedTask;
			}

			TaskCompletionSource met = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_waits.Add((condition, met));

			return met.Task;
		}
	}

	/// <summary>Stops listening.</summary>
	public void Dispose()
	{
		_controller.StateChanged -= OnStateChanged;
	}

	private void OnStateChanged()
	{
		lock (_gate)
		{
			// Read under the lock, so handlers that overlap (a stop on the test's thread, a round ending on a worker's) record the count in the order they read it: the last count recorded is never older than one recorded before it.
			int count = _controller.RunningRounds;

			if (_counts[^1] != count)
			{
				_counts.Add(count);
			}

			_waits.RemoveAll(wait => wait.Condition(_counts) && wait.Met.TrySetResult());
		}
	}
}
