using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Web.Components.Board;

namespace Pyxie.Web.Components.Pages;

/// <summary>The kanban board page of the open project. It follows the pipeline and the open project: whenever cards change, or a project is imported, opened, closed, or removed, here or in another tab, the board is read again. It also draws itself again every minute, so the time a running round has taken keeps up without reading anything.</summary>
public partial class Board : IDisposable
{
	private static readonly TimeSpan s_elapsedRefresh = TimeSpan.FromMinutes(1);

	private readonly CancellationTokenSource _disposeCts = new();

	// Read once: the token of a disposed source cannot be asked for, and a change can still arrive while the page is being disposed.
	private readonly CancellationToken _stopping;
	private readonly ReloadCoalescer _reloads;

	// Why a Retry or Close on a card was refused, by card identifier. Kept until the next change succeeds or the board is read again.
	private readonly Dictionary<int, string> _refusals = [];

	// The lanes as they are drawn, to send the keyboard focus into one.
	private readonly Dictionary<CardLane, BoardLane> _lanes = [];

	// The project the board was last read from, which is the one it shows; null when none was open.
	private Project? _project;
	private List<BoardCard> _cards = [];
	private IReadOnlyList<Category> _categories = [];
	private IReadOnlyList<Agent> _agents = [];
	private CardEditor? _editor;
	private ITimer? _elapsedTimer;
	private bool _loading = true;
	private bool _disposed;
	private string? _openedLink;
	private string? _linkError;

	/// <summary>Gets or sets the card requested by a repository history link.</summary>
	[SupplyParameterFromQuery(Name = "card")]
	public int? LinkedCard { get; set; }

	/// <summary>Gets or sets the project owning the linked card.</summary>
	[SupplyParameterFromQuery(Name = "project")]
	public int? LinkedProject { get; set; }

	// Where the focus goes after a Retry or Close that went through. It waits first for the board to be read again, and then for the board to be drawn from what was read: until then the tile the focus belongs on may not be there yet.
	private BoardFocus? _focusAfterReload;
	private BoardFocus? _focusAfterRender;

	/// <summary>Initializes a new instance of the <see cref="Board"/> class.</summary>
	public Board()
	{
		_stopping = _disposeCts.Token;
		_reloads = new ReloadCoalescer(LoadBoardAsync);
	}

	/// <summary>Gets or sets the card service, supplied by the framework.</summary>
	[Inject]
	public required ICardService CardService { get; set; }

	/// <summary>Gets or sets the category service, supplied by the framework.</summary>
	[Inject]
	public required ICategoryService CategoryService { get; set; }

	/// <summary>Gets or sets the agent service, supplied by the framework.</summary>
	[Inject]
	public required IAgentService AgentService { get; set; }

	/// <summary>Gets or sets the open project, whose board this is, supplied by the framework.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <summary>Gets or sets the controller that holds the displayed project across board operations.</summary>
	[Inject]
	public required PipelineController Controller { get; set; }

	/// <summary>Gets or sets the announcements of changed cards and of changes to the open project, supplied by the framework.</summary>
	[Inject]
	public required PipelineActivity Activity { get; set; }

	/// <summary>Gets or sets the clock whose minutes refresh the elapsed times, supplied by the framework.</summary>
	[Inject]
	public required TimeProvider Time { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Activity.CardsChanged -= OnChanged;
		Activity.ProjectChanged -= OnChanged;
		_elapsedTimer?.Dispose();
		_disposeCts.Cancel();
		_disposeCts.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnInitializedAsync()
	{
		// Subscribe before the first read, so a change that lands while it runs is not missed.
		Activity.CardsChanged += OnChanged;
		Activity.ProjectChanged += OnChanged;
		_elapsedTimer = Time.CreateTimer(OnElapsedRefresh, null, s_elapsedRefresh, s_elapsedRefresh);
		await ReloadAsync();
		_loading = false;
	}

	/// <inheritdoc/>
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		string link = $"{LinkedProject}/{LinkedCard}/{_project?.Id}";
		if (!_loading && _editor is CardEditor editor && LinkedCard is int linkedId && _openedLink != link)
		{
			_openedLink = link;
			int? requestedProject = LinkedProject;
			_linkError = null;
			if (requestedProject is null || requestedProject != _project?.Id)
			{
				_linkError = "Open this card's project from Repository to view its history and media.";
			}
			else
			{
				using var lease = Controller.AcquireProject(requestedProject.Value).Value;
				if (lease is null)
				{
					return;
				}

				ServiceResult<WorkCard> card = await CardService.GetAsync(linkedId, _stopping);
				if (requestedProject == _project?.Id && requestedProject == OpenProject.Current?.Id && ReferenceEquals(editor, _editor))
				{
					if (card.Value is not null)
					{
						await editor.OpenAsync(card.Value);
					}
					else
					{
						_linkError = card.Error;
					}
				}
			}
			StateHasChanged();
		}
		if (_focusAfterRender is not BoardFocus focus)
		{
			return;
		}

		_focusAfterRender = null;
		(CardLane lane, int? cardId) = focus.Resolve(_cards);

		if (!_lanes.TryGetValue(lane, out BoardLane? boardLane) || boardLane is null)
		{
			return;
		}

		try
		{
			await boardLane.FocusAsync(cardId);
		}
		catch (JSDisconnectedException)
		{
		}
	}

	// The card dialog belongs to the project it was opened in: when another project is open, a new dialog replaces it, so a card of the old project is never saved into the new one.
	private int EditorKey => _project?.Id ?? 0;

	// GetBoardAsync orders cards by lane then SortOrder, so filtering keeps each lane in order.
	private List<BoardCard> CardsIn(CardLane lane)
	{
		return _cards.Where(card => card.Card.Lane == lane).ToList();
	}

	// Without an open project there is no board to read. The project is kept with what was read from it, so the page never shows one project's cards under another. What was refused belongs to the board that was read before, so it goes with it.
	private async Task LoadBoardAsync(CancellationToken cancellationToken)
	{
		_refusals.Clear();

		Project? project = OpenProject.Current;
		if (project is null)
		{
			_project = null;
			_lanes.Clear();
			_cards = [];
			_categories = [];
			_agents = [];
			return;
		}

		using var lease = Controller.AcquireProject(project.Id).Value;
		if (lease is null)
		{
			return;
		}

		IReadOnlyList<BoardCard> cards = await CardService.GetBoardAsync(cancellationToken);
		IReadOnlyList<Category> categories = await CategoryService.ListAsync(cancellationToken);
		IReadOnlyList<Agent> agents = await AgentService.ListAsync(cancellationToken);

		_project = project;
		_cards = [.. cards];
		_categories = categories;
		_agents = agents;
	}

	// Reloads are coalesced, so a burst of changes costs one read in flight and one more behind it, never one read per change.
	private async Task ReloadAsync()
	{
		try
		{
			await _reloads.ReloadAsync(_stopping);
		}
		catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
		{
		}
	}

	// Raised on the thread that changed the cards or the open project, which may be a pipeline worker or a background import: the read moves onto the page's own context.
	private void OnChanged()
	{
		_ = InvokeAsync(ReloadAfterChangeAsync);
	}

	// Owns the work started by OnChanged: a failure is reported to the renderer instead of being lost with the task.
	private async Task ReloadAfterChangeAsync()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			// A Retry or Close that went through announced this change. Its focus is taken before the board is read, so it is drawn from a read made after the change, and no earlier read can claim it.
			BoardFocus? focus = _focusAfterReload;
			_focusAfterReload = null;

			await ReloadAsync();
			if (_editor is not null && _project?.Id == OpenProject.Current?.Id)
			{
				await _editor.RefreshAsync();
			}

			if (focus is not null)
			{
				_focusAfterRender = focus;
			}

			StateHasChanged();
		}
		catch (Exception exception) when (!_disposed)
		{
			await DispatchExceptionAsync(exception);
		}
	}

	// The dialog saved, deleted, retried, or closed a card, or the service refused to, or a tile retried or closed one. Announcing it makes this board read again, like every other open board, and wakes idle pipeline workers.
	private Task AnnounceChange()
	{
		Activity.NotifyCardsChanged();

		return Task.CompletedTask;
	}

	// Raised on a thread of the clock's own: the render moves onto the page's context. Nothing is read, since only the elapsed times have moved on.
	private void OnElapsedRefresh(object? state)
	{
		_ = InvokeAsync(RefreshElapsed);
	}

	// Owns the work started by the timer: it only draws the page again, so there is nothing to lose with the task.
	private void RefreshElapsed()
	{
		if (!_disposed)
		{
			StateHasChanged();
		}
	}

	private Task OpenEditor(WorkCard? card, int shownProject)
	{
		return _editor?.Project?.Id == shownProject ? _editor.OpenAsync(card) : Task.CompletedTask;
	}

	private Task RetryAsync(WorkCard card, int shownProject)
	{
		return ChangeAsync(card, shownProject, CardService.RetryAsync, BoardFocus.OnCard(card));
	}

	private Task CloseAsync(WorkCard card, int shownProject)
	{
		return ChangeAsync(card, shownProject, CardService.CloseAsync, BoardFocus.InPlaceOf(card, _cards));
	}

	// A person retried or closed a card from its tile. Success is announced, which reads the board again on every open board, and moves the keyboard focus on once it has: the button that was pressed goes with the change. A refusal is shown on the card it was about, announces nothing, since nothing changed, and leaves the focus on the button.
	private async Task ChangeAsync(WorkCard card, int shownProject, Func<int, CancellationToken, Task<ServiceResult>> change, BoardFocus focus)
	{
		if (shownProject != _project?.Id)
		{
			return;
		}

		ServiceResult result;

		try
		{
			var project = Controller.AcquireProject(shownProject);
			using var lease = project.Value;
			result = lease is null ? ServiceResult.Failure(project.Error ?? "The project changed.") : await change(card.Id, _stopping);
		}
		catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
		{
			return;
		}

		if (result.Succeeded)
		{
			_refusals.Clear();
			_focusAfterReload = focus;
			await AnnounceChange();
		}
		else
		{
			_refusals[card.Id] = result.Error ?? "The card could not be changed.";
		}
	}
}
