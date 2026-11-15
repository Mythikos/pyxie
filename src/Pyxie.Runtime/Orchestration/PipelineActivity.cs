namespace Pyxie.Runtime.Orchestration;

/// <summary>Announces that cards changed, so every open board refreshes and idle pipeline workers look for work at once, and that a project was imported, opened, or closed, so whatever depends on the open project (the header's Start, the pages) follows. The pipeline announces its own changes; pages and the project services announce the rest.</summary>
public sealed class PipelineActivity
{
	private readonly PipelineSignal _signal = new();

	/// <summary>Raised after cards changed. Handlers run on the thread that announced the change and must not throw.</summary>
	public event Action? CardsChanged;

	/// <summary>Raised after a project was imported, opened, or closed. Handlers run on the thread that announced the change and must not throw.</summary>
	public event Action? ProjectChanged;

	/// <summary>Announces that cards changed.</summary>
	public void NotifyCardsChanged()
	{
		_signal.Pulse();
		CardsChanged?.Invoke();
	}

	/// <summary>Announces that a project was imported, opened, or closed.</summary>
	public void NotifyProjectChanged()
	{
		_signal.Pulse();
		ProjectChanged?.Invoke();
	}

	/// <summary>Gets a task that completes the next time cards or the open project change.</summary>
	/// <returns>The task.</returns>
	internal Task NextChangeAsync()
	{
		return _signal.NextAsync();
	}
}
