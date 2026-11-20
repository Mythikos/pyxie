using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Projects;

namespace Pyxie.Runtime.Orchestration;

/// <summary>Starts and stops the pipeline, and keeps the open project from changing under it. The pipeline starts stopped every time Pyxie launches, so launching never spends harness usage, and it starts only while a project is open. Stopping kills the rounds that are running and records them as interrupted; starting again resumes them first.</summary>
/// <remarks>Projects change (import, open, close, remove) only between <see cref="BeginProjectChangeAsync"/> and <see cref="EndProjectChange"/>, one at a time. A change is refused while the pipeline is started, waits while the stopped pipeline still finishes work it began (<see cref="TryBeginWork"/>), and <see cref="Start"/> is refused while a change is under way. One lock decides both, so the project the pipeline works on never changes while it works. How many rounds are running (<see cref="RunningRounds"/>) is only reported: it takes no part in any of those decisions.</remarks>
public sealed class PipelineController
{
	private readonly PipelineSignal _signal = new();

	// Wakes a pending project change when work ends or an explicit pipeline start takes precedence.
	private readonly PipelineSignal _drainChanged = new();
	private readonly Lock _gate = new();
	private readonly OpenProject _openProject;
	private bool _running;
	private string? _stopReason;
	private bool _projectChanging;
	private bool _projectChangePending;
	private int _activeWork;
	private int _runningRounds;

	/// <summary>Initializes a new instance of the <see cref="PipelineController"/> class.</summary>
	/// <param name="openProject">The open project, which the pipeline needs to start.</param>
	public PipelineController(OpenProject openProject)
	{
		_openProject = openProject;
	}

	/// <summary>Raised after the pipeline was started or stopped, or the number of running rounds (<see cref="RunningRounds"/>) changed. Handlers run on the caller's thread, which for a round is a pipeline worker's, and must not throw.</summary>
	public event Action? StateChanged;

	/// <summary>Gets a value indicating whether the pipeline is started.</summary>
	public bool IsRunning
	{
		get
		{
			lock (_gate)
			{
				return _running;
			}
		}
	}

	/// <summary>Gets why the pipeline stopped by itself, such as Docker not running; <see langword="null"/> when a person stopped it or it is running.</summary>
	public string? StopReason
	{
		get
		{
			lock (_gate)
			{
				return _stopReason;
			}
		}
	}

	/// <summary>Gets how many rounds the pipeline is running right now: the cards a pipeline worker has a round open for, from the moment it claims the card until the card's run ends. It is zero while the pipeline is stopped and every round it interrupted has been recorded.</summary>
	public int RunningRounds
	{
		get
		{
			lock (_gate)
			{
				return _runningRounds;
			}
		}
	}

	/// <summary>Starts the pipeline. Does nothing when it is already started.</summary>
	/// <returns>A failure when no project is open, or while a project is being imported, opened, closed, or removed.</returns>
	public ServiceResult Start()
	{
		lock (_gate)
		{
			if (_running)
			{
				return ServiceResult.Success();
			}

			if (_projectChanging)
			{
				return ServiceResult.Failure("A project is being opened or closed. Try again in a moment.");
			}

			if (_openProject.Current is null)
			{
				return ServiceResult.Failure("No project is open.");
			}

			_running = true;
			_stopReason = null;
			if (_projectChangePending)
			{
				_drainChanged.Pulse();
			}
		}

		Changed();

		return ServiceResult.Success();
	}

	/// <summary>Stops the pipeline. Does nothing when it is already stopped.</summary>
	public void Stop()
	{
		lock (_gate)
		{
			if (!_running)
			{
				return;
			}

			_running = false;
			_stopReason = null;
		}

		Changed();
	}

	/// <summary>Stops the pipeline because it cannot go on, and keeps the reason for the header to show until the pipeline starts again.</summary>
	/// <param name="reason">Why, in words for a person.</param>
	internal void StopBecause(string reason)
	{
		lock (_gate)
		{
			if (!_running)
			{
				return;
			}

			_running = false;
			_stopReason = reason;
		}

		Changed();
	}

	/// <summary>Begins changing projects (importing, opening, closing, or removing one), once the stopped pipeline has finished the work it began: a session that is still recording its interrupted rounds, or a pass that marks leftover rounds interrupted. The caller must call <see cref="EndProjectChange"/> once it is done, however it ends.</summary>
	/// <param name="cancellationToken">A token to stop waiting.</param>
	/// <returns>A failure while the pipeline is started, or while another change is under way.</returns>
	internal async Task<ServiceResult> BeginProjectChangeAsync(CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			if (_running)
			{
				return ServiceResult.Failure("Stop the pipeline first.");
			}

			if (_projectChangePending || _projectChanging)
			{
				return ServiceResult.Failure("Another project is being imported, opened, closed, or removed. Try again once it is done.");
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (_activeWork == 0)
			{
				_projectChanging = true;
				return ServiceResult.Success();
			}

			_projectChangePending = true;
		}

		try
		{
			while (true)
			{
				Task stateChanged;
				lock (_gate)
				{
					if (_running)
					{
						return ServiceResult.Failure("Stop the pipeline first.");
					}

					if (_activeWork == 0)
					{
						_projectChanging = true;
						return ServiceResult.Success();
					}

					stateChanged = _drainChanged.NextAsync();
				}

				await stateChanged.WaitAsync(cancellationToken);
			}
		}
		finally
		{
			lock (_gate)
			{
				_projectChangePending = false;
			}
		}
	}

	/// <summary>Ends a change that <see cref="BeginProjectChangeAsync"/> began.</summary>
	internal void EndProjectChange()
	{
		lock (_gate)
		{
			_projectChanging = false;
		}
	}

	/// <summary>Holds the named open project across a caller's service operations, including while the pipeline is stopped.</summary>
	/// <param name="projectId">The project the caller inspected.</param>
	/// <returns>A lease to dispose after the operation, or a refusal if the project has changed.</returns>
	public ServiceResult<ProjectLease> AcquireProject(int projectId)
	{
		lock (_gate)
		{
			if (_projectChanging || _projectChangePending || _openProject.Current is not Project project || project.Id != projectId)
			{
				return ServiceResult<ProjectLease>.Failure("The requested project is closed or changing. Check the project list.");
			}

			_activeWork++;
			return ServiceResult<ProjectLease>.Success(new ProjectLease(project, this));
		}
	}

	/// <summary>
	/// Begins work in the open project: a pipeline session, or recovering rounds. The open project cannot change until <see cref="EndWork"/>, which the caller must call however the work ends.
	/// </summary>
	/// <param name="requireRunning">Whether the work needs the pipeline to be started.</param>
	/// <returns><see langword="false"/> while the project is being changed, or when the pipeline is stopped and the work needs it started.</returns>
	internal bool TryBeginWork(bool requireRunning)
	{
		lock (_gate)
		{
			if (_projectChanging || (!_running && (requireRunning || _projectChangePending)))
			{
				return false;
			}

			_activeWork++;

			return true;
		}
	}

	/// <summary>Ends work that <see cref="TryBeginWork"/> began.</summary>
	internal void EndWork()
	{
		lock (_gate)
		{
			_activeWork--;
			if (_projectChangePending && _activeWork == 0)
			{
				_drainChanged.Pulse();
			}
		}
	}

	/// <summary>Counts a round that a pipeline worker has open. The caller must call <see cref="RoundEnded"/> once no round is open for the card any more, however its run ends. It raises <see cref="StateChanged"/> but, unlike a start or a stop, does not wake <see cref="NextChangeAsync"/>: nothing the orchestrator waits on has changed.</summary>
	internal void RoundStarted()
	{
		lock (_gate)
		{
			_runningRounds++;
		}

		StateChanged?.Invoke();
	}

	/// <summary>Stops counting a round that <see cref="RoundStarted"/> counted, once no round is open for its card any more: the card's run ended, or the round was interrupted.</summary>
	internal void RoundEnded()
	{
		lock (_gate)
		{
			_runningRounds--;
		}

		StateChanged?.Invoke();
	}

	/// <summary>Gets a task that completes the next time the pipeline is started or stopped.</summary>
	/// <returns>The task.</returns>
	internal Task NextChangeAsync()
	{
		return _signal.NextAsync();
	}

	private void Changed()
	{
		_signal.Pulse();
		StateChanged?.Invoke();
	}
}
