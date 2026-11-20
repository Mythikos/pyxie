using Pyxie.Data.Transfer;

namespace Pyxie.Runtime.Orchestration;

/// <summary>Announces that cards changed, so every open board refreshes and idle pipeline workers look for work at once, and that a project was imported, opened, closed, or removed, so whatever depends on the open project (the header's Start, the pages) follows. The pipeline announces its own changes; pages and the project services announce the rest.</summary>
public sealed class PipelineActivity
{
	private readonly PipelineSignal _signal = new();

	/// <summary>Raised after cards changed. Handlers run on the thread that announced the change and must not throw.</summary>
	public event Action? CardsChanged;

	/// <summary>Raised after a project was imported, opened, closed, or removed. Handlers run on the thread that announced the change and must not throw.</summary>
	public event Action? ProjectChanged;

	/// <summary>Raised when an external editor changes a settings section.</summary>
	public event Action<int, TransferSection>? SettingsChanged;

	/// <summary>Refreshes the changed project's settings section and wakes boards and workers.</summary>
	/// <param name="projectId">The affected project.</param>
	/// <param name="section">The affected section.</param>
	public void NotifySettingsChanged(int projectId, TransferSection section)
	{
		SettingsChanged?.Invoke(projectId, section);
		NotifyCardsChanged();
	}

	/// <summary>Announces that cards changed.</summary>
	public void NotifyCardsChanged()
	{
		_signal.Pulse();
		CardsChanged?.Invoke();
	}

	/// <summary>Announces that a project was imported, opened, closed, or removed.</summary>
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
