using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Services.Rules;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Media;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Orchestration;

/// <summary>The pipeline: a hosted service that, while started (see <see cref="PipelineController"/>), keeps up to <see cref="PipelineOptions.Workers"/> cards of the open project running. Each worker claims a card and runs its rounds one after another until the card's run ends, recording every result through <see cref="ICardPipelineService"/>. Stopping the pipeline or Pyxie kills the running rounds and records them as interrupted; they resume first when the pipeline starts.</summary>
/// <remarks>Rounds a previous run of Pyxie left open in a project are marked interrupted once per project: as soon as it is open (at launch, or when it is opened while the pipeline is stopped), and in any case before the pipeline claims anything in it. Workspaces that cards no longer need are removed every time the pipeline starts.</remarks>
public sealed partial class PipelineOrchestrator : BackgroundService
{
	private const string StoppedNote = "The pipeline was stopped while this round was running.";
	private const string ShutdownNote = "Pyxie shut down while this round was running.";

	private readonly IServiceScopeFactory _scopes;
	private readonly OpenProject _openProject;
	private readonly PipelineController _controller;
	private readonly PipelineActivity _activity;
	private readonly CardSandboxes _cards;
	private readonly ISandboxes _sandboxes;
	private readonly SandboxImageBuilds _imageBuilds;
	private readonly TimeProvider _timeProvider;
	private readonly PipelineOptions _options;
	private readonly ILogger<PipelineOrchestrator> _logger;

	// The project whose leftover rounds were marked interrupted. Only the loop in ExecuteAsync reads and writes it.
	private int? _recoveredProjectId;

	// How many image builds had succeeded when unused images were last removed. Only the loop in ExecuteAsync reads and writes it.
	private int _sweptBuilds;

	/// <summary>Initializes a new instance of the <see cref="PipelineOrchestrator"/> class.</summary>
	/// <param name="scopes">Creates a scope per operation for the scoped data services.</param>
	/// <param name="openProject">The open project, which the pipeline works on.</param>
	/// <param name="controller">Whether the pipeline is started; it also keeps the open project from changing while the pipeline works.</param>
	/// <param name="activity">Announces card changes, and wakes idle workers when people change cards.</param>
	/// <param name="cards">Runs cards' rounds in sandboxes and removes what finished cards left.</param>
	/// <param name="sandboxes">Makes the sandboxes; the pipeline does not start while it cannot.</param>
	/// <param name="imageBuilds">The builds of projects' images, whose leftovers the pipeline removes while no round runs.</param>
	/// <param name="timeProvider">The clock idle workers wait on.</param>
	/// <param name="options">The pipeline options.</param>
	/// <param name="logger">The logger.</param>
	public PipelineOrchestrator(IServiceScopeFactory scopes, OpenProject openProject, PipelineController controller, PipelineActivity activity, CardSandboxes cards, ISandboxes sandboxes, SandboxImageBuilds imageBuilds, TimeProvider timeProvider, IOptions<PipelineOptions> options, ILogger<PipelineOrchestrator> logger)
	{
		_scopes = scopes;
		_openProject = openProject;
		_controller = controller;
		_activity = activity;
		_cards = cards;
		_sandboxes = sandboxes;
		_imageBuilds = imageBuilds;
		_sweptBuilds = imageBuilds.SucceededBuilds;
		_timeProvider = timeProvider;
		_options = options.Value;
		_logger = logger;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			// Taken before anything is checked, so a change that lands meanwhile still wakes the waits below.
			Task stateChanged = _controller.NextChangeAsync();
			Task activityChanged = _activity.NextChangeAsync();
			Task buildEnded = _imageBuilds.NextEndAsync();

			if (!_controller.IsRunning)
			{
				// A project opened while the pipeline is stopped announces itself, which wakes this wait, so its leftover rounds are marked interrupted at once. A build that ends wakes it too, so what the build left is removed at once.
				await RecoverWhileStoppedAsync(stoppingToken);
				await RemoveUnusedImagesWhileStoppedAsync(stoppingToken);
				await Task.WhenAny(stateChanged, activityChanged, buildEnded).WaitAsync(stoppingToken);
				continue;
			}

			if (!_controller.TryBeginWork(requireRunning: true))
			{
				continue;
			}

			try
			{
				await RunStartedAsync(stateChanged, stoppingToken);
			}
			finally
			{
				_controller.EndWork();
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Marked {Count} rounds left running by the previous run of Pyxie as interrupted.")]
	private static partial void LogRecovered(ILogger logger, int count);

	[LoggerMessage(Level = LogLevel.Warning, Message = "The pipeline stopped: {Reason}")]
	private static partial void LogSandboxesUnavailable(ILogger logger, string reason);

	[LoggerMessage(Level = LogLevel.Information, Message = "Removed the workspaces of cards {CardIds}, which no longer need them.")]
	private static partial void LogRemovedWorkspaces(ILogger logger, string cardIds);

	[LoggerMessage(Level = LogLevel.Error, Message = "Could not mark the rounds left running by the previous run of Pyxie as interrupted; the pipeline tries again before it claims any card.")]
	private static partial void LogRecoveryFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the workspaces of cards that no longer need them; they are removed the next time the pipeline starts.")]
	private static partial void LogReconcileFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the sandbox images and build cache that nothing needs; they are removed the next time the pipeline starts.")]
	private static partial void LogImageCleanupFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the workspace of merged card {CardId}; it is removed the next time the pipeline starts.")]
	private static partial void LogCleanupFailed(ILogger logger, int cardId, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "Claiming the next card failed; trying again at the next poll.")]
	private static partial void LogClaimFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "The run of card {CardId} ended unexpectedly; the card's open round, if any, is marked interrupted the next time Pyxie starts.")]
	private static partial void LogCardFailed(ILogger logger, int cardId, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "Round {RoundId} of card {CardId} failed unexpectedly.")]
	private static partial void LogRoundFailed(ILogger logger, int roundId, int cardId, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "Could not record the result of round {RoundId} of card {CardId}: {Error}")]
	private static partial void LogRecordFailed(ILogger logger, int roundId, int cardId, string error);

	[LoggerMessage(Level = LogLevel.Error, Message = "Could not record round {RoundId} of card {CardId} as interrupted: {Error}")]
	private static partial void LogInterruptFailed(ILogger logger, int roundId, int cardId, string error);

	// Runs while the pipeline is started and holds the open project. Rounds left open by a previous run must be marked interrupted before anything is claimed; when that fails, it is tried again at the next poll.
	private async Task RunStartedAsync(Task stateChanged, CancellationToken stoppingToken)
	{
		// Starting needs an open project, and the project cannot change while the pipeline is started, so this only guards against claiming without one.
		if (_openProject.Current is not Project project)
		{
			await stateChanged.WaitAsync(stoppingToken);
			return;
		}

		if (!await TryRecoverAsync(project, stoppingToken))
		{
			await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), _timeProvider, stoppingToken);
			return;
		}

		await RunSessionAsync(project.Folder, stoppingToken);
	}

	// Marks the open project's leftover rounds interrupted while the pipeline is stopped. A project change under way is left alone; it announces itself when done, which comes back here. A project already recovered is not held at all, so card changes that wake this loop never make a project change wait.
	private async Task RecoverWhileStoppedAsync(CancellationToken stoppingToken)
	{
		if (_openProject.Current is not Project open || open.Id == _recoveredProjectId || !_controller.TryBeginWork(requireRunning: false))
		{
			return;
		}

		try
		{
			if (_openProject.Current is Project project)
			{
				await TryRecoverAsync(project, stoppingToken);
			}
		}
		finally
		{
			_controller.EndWork();
		}
	}

	// Rounds still open in a project that was just opened belonged to a Pyxie that stopped without recording them; within one run of Pyxie every session records its rounds before it ends, so a project needs this once. A failure (the database is unavailable, say) is logged, never fatal to the host. The caller holds the open project.
	private async Task<bool> TryRecoverAsync(Project project, CancellationToken stoppingToken)
	{
		if (_recoveredProjectId == project.Id)
		{
			return true;
		}

		try
		{
			await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
			ICardPipelineService pipeline = scope.ServiceProvider.GetRequiredService<ICardPipelineService>();
			await using ProjectDbContext context = scope.ServiceProvider.GetRequiredService<IProjectDbContextSource>().CreateFor(project);
			List<CardRound> openRounds = await context.Rounds.AsNoTracking().Where(round => round.Outcome == null).ToListAsync(stoppingToken);
			foreach (CardRound round in openRounds)
			{
				int roundId = round.Id;
				MediaCollection media = await RoundMedia.ReadReceiptAsync(project.Folder, roundId, stoppingToken);
				string role = SandboxNames.RoleName(round.Kind switch { RoundKind.Work => SandboxRole.Worker, RoundKind.Review => SandboxRole.Review, RoundKind.Resolve => SandboxRole.Resolve, _ => SandboxRole.Gates });
				string folder = WorkspaceLayout.SandboxFolder(project.Folder, round.CardId, role);
				string marker = Path.Combine(folder, "media-round");
				if (!media.Complete && File.Exists(marker) && await File.ReadAllTextAsync(marker, stoppingToken) == roundId.ToString(System.Globalization.CultureInfo.InvariantCulture) && Directory.Exists(Path.Combine(folder, "out", "media")))
				{
					await _sandboxes.QuiesceCardAsync(SandboxNames.ProjectKey(project.Folder), round.CardId, stoppingToken);
					media = await scope.ServiceProvider.GetRequiredService<RoundMedia>().CollectAsync(project, round.CardId, roundId, Path.Combine(folder, "out"), null, stoppingToken);
				}
				if (media.Files.Count > 0 || media.Problems.Count > 0)
				{
					ServiceResult result = await pipeline.InterruptAsync(roundId, new RoundReport("Pyxie stopped while this round was running.", string.Join('\n', media.Problems)) { Media = media.Files }, stoppingToken);
					if (!result.Succeeded)
					{
						throw new InvalidOperationException(result.Error);
					}
				}
			}
			await pipeline.RecoverInterruptedAsync(stoppingToken);
			if (openRounds.Count > 0)
			{
				LogRecovered(_logger, openRounds.Count);
				_activity.NotifyCardsChanged();
			}

			_recoveredProjectId = project.Id;

			return true;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogRecoveryFailed(_logger, exception);

			return false;
		}
	}

	// Runs cards until the pipeline is stopped or Pyxie shuts down, then waits for every running card to give back its round.
	private async Task RunSessionAsync(string projectFolder, CancellationToken stoppingToken)
	{
		using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		List<Task> running = [];

		// Every round runs in a sandbox, so a pipeline that cannot make one stops at once, saying why, rather than failing each card it picks up.
		ServiceResult available = await _sandboxes.CheckAvailableAsync(stoppingToken);
		if (!available.Succeeded)
		{
			LogSandboxesUnavailable(_logger, available.Error ?? string.Empty);
			_controller.StopBecause(available.Error ?? "Sandboxes cannot be made.");

			return;
		}

		await RemoveAbandonedWorkspacesAsync(projectFolder, stoppingToken);
		await RemoveUnusedImagesAsync(projectFolder, stoppingToken);

		try
		{
			while (_controller.IsRunning && !stoppingToken.IsCancellationRequested)
			{
				running.RemoveAll(task => task.IsCompleted);

				// Taken before the claim, so a change that happens while claiming still wakes the wait below.
				Task stateChanged = _controller.NextChangeAsync();
				Task cardsChanged = _activity.NextChangeAsync();

				if (running.Count < _options.Workers && await ClaimAsync(stoppingToken) is PipelineWork work)
				{
					running.Add(RunCardAsync(projectFolder, work, session.Token, stoppingToken));
					_activity.NotifyCardsChanged();
					continue;
				}

				Task poll = Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), _timeProvider, stoppingToken);
				await Task.WhenAny([stateChanged, cardsChanged, poll, .. running]);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			await session.CancelAsync();
			await Task.WhenAll(running);
		}
	}

	// A claim that fails (the database is unavailable, say) is logged and retried at the next poll rather than ending the pipeline. Nothing is claimed without an open project.
	private async Task<PipelineWork?> ClaimAsync(CancellationToken stoppingToken)
	{
		if (_openProject.Current is null)
		{
			return null;
		}

		try
		{
			await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

			return await scope.ServiceProvider.GetRequiredService<ICardPipelineService>().ClaimNextAsync(stoppingToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogClaimFailed(_logger, exception);

			return null;
		}
	}

	// Never throws: a round that fails unexpectedly is recorded on the card as an error, and anything that fails beyond that is logged, so one card never stops the others.
	private async Task RunCardAsync(string projectFolder, PipelineWork first, CancellationToken session, CancellationToken stoppingToken)
	{
		try
		{
			Repository? mergedInto;

			// A round is open for the card from its claim until its run ends, however that happens. Recording a result closes a round and opens the next in one step, so the card counts once, however many rounds it runs.
			_controller.RoundStarted();
			try
			{
				mergedInto = await RunRoundsAsync(first, session, stoppingToken);
			}
			finally
			{
				_controller.RoundEnded();
			}

			// No round is open any more, so the card does not count while its workspace is removed.
			if (mergedInto is not null)
			{
				await RemoveWorkspaceAsync(projectFolder, mergedInto, first.CardId);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogCardFailed(_logger, first.CardId, exception);
		}
	}

	// Runs the card's rounds one after another until its run ends. Returns the repository the card was merged into when the run ended with its merge, which is when the card's workspace is no longer needed; otherwise null.
	private async Task<Repository?> RunRoundsAsync(PipelineWork first, CancellationToken session, CancellationToken stoppingToken)
	{
		PipelineWork? work = first;
		Repository? mergedInto = null;

		while (work is not null)
		{
			if (session.IsCancellationRequested)
			{
				await InterruptAsync(work, stoppingToken.IsCancellationRequested ? ShutdownNote : StoppedNote);
				return null;
			}

			RoundVerdict verdict;
			Repository? repository = null;

			try
			{
				await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
				(verdict, repository) = await scope.ServiceProvider.GetRequiredService<RoundExecutor>().RunAsync(work, session);
			}
			catch (OperationCanceledException) when (session.IsCancellationRequested)
			{
				await InterruptAsync(work, stoppingToken.IsCancellationRequested ? ShutdownNote : StoppedNote);
				return null;
			}
			catch (SandboxUnavailableException exception)
			{
				// Nothing is wrong with the card: the round is given back, to resume when the pipeline starts again, and the pipeline stops, saying why.
				LogSandboxesUnavailable(_logger, exception.Message);
				await InterruptAsync(work, $"{exception.Message} The round resumes when the pipeline starts again.");
				_controller.StopBecause(exception.Message);
				return null;
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogRoundFailed(_logger, work.RoundId, work.CardId, exception);
				verdict = RoundVerdict.Errored($"Pyxie failed while running the round: {exception.Message}", exception.ToString());
			}

			PipelineWork? next = await RecordAsync(work, verdict);
			_activity.NotifyCardsChanged();

			if (next is null && work.Kind == RoundKind.Merge && verdict.Result == RoundResult.Succeeded)
			{
				mergedInto = repository;
			}

			work = next;
		}

		return mergedInto;
	}

	// The result lands even while stopping: the round has run, and its outcome should not be lost.
	private async Task<PipelineWork?> RecordAsync(PipelineWork work, RoundVerdict verdict)
	{
		if (_openProject.Current is Project project)
		{
			MediaCollection media = await RoundMedia.ReadReceiptAsync(project.Folder, work.RoundId, CancellationToken.None);
			if (media.Files.Count > 0)
			{
				verdict = verdict with { Media = media.Files };
			}
		}
		await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
		ICardPipelineService pipeline = scope.ServiceProvider.GetRequiredService<ICardPipelineService>();

		ServiceResult<PipelineWork?> recorded = await pipeline.RecordResultAsync(work.RoundId, verdict.Result, verdict.ToRoundReport(), CancellationToken.None);
		if (recorded.Succeeded)
		{
			return recorded.Value;
		}

		// A result the pipeline refuses would leave the round open forever, so the card stops for a person instead.
		LogRecordFailed(_logger, work.RoundId, work.CardId, recorded.Error ?? string.Empty);
		RoundVerdict fallback = RoundVerdict.Blocked($"Pyxie could not record the round's result: {recorded.Error}", verdict.Report) with { Media = verdict.Media };
		ServiceResult<PipelineWork?> blocked = await pipeline.RecordResultAsync(work.RoundId, fallback.Result, fallback.ToRoundReport(), CancellationToken.None);
		if (!blocked.Succeeded)
		{
			LogRecordFailed(_logger, work.RoundId, work.CardId, blocked.Error ?? string.Empty);
		}

		return null;
	}

	private async Task InterruptAsync(PipelineWork work, string note)
	{
		await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
		MediaCollection media = _openProject.Current is Project project ? await RoundMedia.ReadReceiptAsync(project.Folder, work.RoundId, CancellationToken.None) : MediaCollection.Empty;
		ServiceResult interrupted = await scope.ServiceProvider.GetRequiredService<ICardPipelineService>().InterruptAsync(work.RoundId, new RoundReport(note, string.Join('\n', media.Problems)) { Media = media.Files }, CancellationToken.None);
		if (!interrupted.Succeeded)
		{
			LogInterruptFailed(_logger, work.RoundId, work.CardId, interrupted.Error ?? string.Empty);
		}

		_activity.NotifyCardsChanged();
	}

	private async Task RemoveWorkspaceAsync(string projectFolder, Repository repository, int cardId)
	{
		try
		{
			await _cards.RemoveAsync(projectFolder, repository, cardId, CancellationToken.None);
		}
		catch (Exception exception) when (exception is GitException or SandboxException or SandboxUnavailableException or IOException or UnauthorizedAccessException)
		{
			LogCleanupFailed(_logger, cardId, exception);
		}
	}

	// Cards deleted from the Queue, merged, or closed leave sandboxes and branches behind when Pyxie stopped before removing them.
	private async Task RemoveAbandonedWorkspacesAsync(string projectFolder, CancellationToken stoppingToken)
	{
		try
		{
			await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
			Repository? repository = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().GetAsync(stoppingToken);
			if (repository is null)
			{
				return;
			}

			IReadOnlyList<BoardCard> board = await scope.ServiceProvider.GetRequiredService<ICardService>().GetBoardAsync(stoppingToken);
			HashSet<int> unfinished = [.. board.Where(card => !CardLifecycleRules.IsFinished(card.Card.Lane)).Select(card => card.Card.Id)];

			IReadOnlyList<int> removed = await _cards.RemoveAbandonedAsync(projectFolder, repository, unfinished, stoppingToken);
			if (removed.Count > 0)
			{
				LogRemovedWorkspaces(_logger, string.Join(", ", removed));
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogReconcileFailed(_logger, exception);
		}
	}

	// A build that succeeded since the last removal can leave images and build cache nothing needs. While the pipeline is stopped no round can be starting a sandbox, so they go at once; the open project is held meanwhile.
	private async Task RemoveUnusedImagesWhileStoppedAsync(CancellationToken stoppingToken)
	{
		if (_imageBuilds.SucceededBuilds == _sweptBuilds || !_controller.TryBeginWork(requireRunning: false))
		{
			return;
		}

		try
		{
			if (_openProject.Current is Project project)
			{
				await RemoveUnusedImagesAsync(project.Folder, stoppingToken);
			}
		}
		finally
		{
			_controller.EndWork();
		}
	}

	// Runs only from the loop in ExecuteAsync, before a session's first round or while the pipeline is stopped, because a round that already found its image must not find it gone when it starts the sandbox. A build that is running makes it do nothing; the build's end wakes the loop to try again. A failure (Docker not running, say) is logged and not tried again before the next session or build.
	private async Task RemoveUnusedImagesAsync(string projectFolder, CancellationToken stoppingToken)
	{
		int builds = _imageBuilds.SucceededBuilds;
		try
		{
			await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
			SandboxSettings settings = await scope.ServiceProvider.GetRequiredService<ISandboxSettingsService>().GetAsync(stoppingToken);
			if (!await _sandboxes.RemoveUnusedImagesAsync(SandboxNames.ProjectKey(projectFolder), settings.Dockerfile, stoppingToken))
			{
				return;
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogImageCleanupFailed(_logger, exception);
		}

		_sweptBuilds = builds;
	}
}
