using Blazorise;

using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Services.Rules;
using Pyxie.Web.Components.Board.Models;

namespace Pyxie.Web.Components.Board;

/// <summary>
/// A modal for creating, editing, retrying, and deleting a card in the Queue, and for reviewing a card an agent has picked up and closing it out once it is complete.
/// </summary>
public partial class CardEditor : IDisposable
{
	private readonly CancellationTokenSource _disposeCts = new();

	// Every footer button has the same width, so the Back button of a confirmation sits exactly where the button that opened it was, and the confirming button where Back was. A second click at the pointer's position then cancels the confirmation instead of confirming it.
	private readonly IFluentSizingStyle _actionWidth = Width.Px().Min(104);
	private WorkCard? _card;
	private IReadOnlyList<CardRound> _rounds = [];
	private CardHistorySummary _summary = CardHistorySummary.Empty;
	private CardFormModel _form = CardFormModel.CreateNew();
	private CardEditorMode _mode = CardEditorMode.Create;
	private Button? _confirmOpener;
	private Button? _confirmBack;
	private string? _error;
	private bool _attempted;
	private bool _visible;
	private bool _busy;
	private bool _confirming;
	private bool _focusConfirmBack;
	private bool _focusConfirmOpener;

	/// <summary>Gets or sets the categories offered by the category field.</summary>
	[Parameter]
	public IReadOnlyList<Category> Categories { get; set; } = [];

	/// <summary>Gets or sets the agents offered by the worker and reviewer fields.</summary>
	[Parameter]
	public IReadOnlyList<Agent> Agents { get; set; } = [];

	/// <summary>Gets or sets the callback raised after a card is created, updated, deleted, retried, or closed, and after the service refuses a change, so the board shows what the database holds.</summary>
	[Parameter]
	public EventCallback Changed { get; set; }

	/// <summary>Gets or sets the card service, supplied by the framework.</summary>
	[Inject]
	public required ICardService CardService { get; set; }

	/// <summary>Opens the editor. A card that is no longer in the Queue opens for review only.</summary>
	/// <param name="card">The card to show, or <see langword="null"/> to create a new card.</param>
	/// <returns>A task that completes when the editor has been shown.</returns>
	public async Task OpenAsync(WorkCard? card)
	{
		Show(card);
		_error = null;
		_busy = false;
		await LoadHistoryAsync();
		_visible = true;
		StateHasChanged();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_disposeCts.Cancel();
		_disposeCts.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		// Starting or cancelling a confirmation replaces the button that was just pressed, which would drop keyboard focus to the page. Focus goes to Back when the confirmation starts, so that pressing Enter again cancels instead of confirming, and back to the button that started it when the confirmation is cancelled.
		if (_focusConfirmBack && _confirmBack is not null)
		{
			_focusConfirmBack = false;
			await _confirmBack.Focus(scrollToElement: false);
		}
		else if (_focusConfirmOpener && _confirmOpener is not null)
		{
			_focusConfirmOpener = false;
			await _confirmOpener.Focus(scrollToElement: false);
		}
	}

	// A card shown read-only is rendered as plain text from the card itself: a disabled select still reacts to the keyboard once it has focus.
	private bool ReadOnly => _mode is CardEditorMode.Review or CardEditorMode.Gone;

	private WorkCard? ReviewedCard => ReadOnly ? _card : null;

	private bool CanCloseCard => _mode == CardEditorMode.Review && _card is not null && CardLifecycleRules.CanClose(_card.Lane);

	private bool CanRetry => _mode == CardEditorMode.Edit && _card is not null && CardLifecycleRules.CanRetry(_card.Lane, _card.State);

	private string Title => _mode switch
	{
		CardEditorMode.Create => "New card",
		CardEditorMode.Edit => "Edit card",
		_ => "Card",
	};

	private string? LaneLabel => _mode == CardEditorMode.Review && _card is not null ? _card.Lane.DisplayName() : null;

	// Why the pipeline stopped the card, shown while the person can retry it.
	private string? StopReason => CanRetry ? _summary.StopReason : null;

	private string AttemptsText => _card is null ? string.Empty : AttemptProgress.Text(_card.Attempt, _card.AttemptLimit);

	// Shows a card as the database holds it: a new card, one in the Queue that people can edit, or any other card for review only. The fields start from its stored values.
	private void Show(WorkCard? card)
	{
		_card = card;
		_mode = card is null ? CardEditorMode.Create : CardLifecycleRules.CanEdit(card.Lane) ? CardEditorMode.Edit : CardEditorMode.Review;
		_form = card is null ? CardFormModel.CreateNew() : CardFormModel.FromCard(card);
		_attempted = false;
		_confirming = false;
	}

	// The history is shown beside the fields of any stored card; a new card has none, and a failed read simply shows none. The summary follows it, and it follows the card's state, so it is read again whenever the card was.
	private async Task LoadHistoryAsync()
	{
		_rounds = [];
		_summary = CardHistorySummary.Empty;

		if (_card is null)
		{
			return;
		}

		try
		{
			ServiceResult<IReadOnlyList<CardRound>> history = await CardService.GetHistoryAsync(_card.Id, _disposeCts.Token);
			_rounds = history.Value ?? [];
		}
		catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
		{
		}

		_summary = CardHistorySummary.From(_card.State, _rounds.Select(RoundSummary.From));
	}

	private Task HideAsync()
	{
		_visible = false;
		return Task.CompletedTask;
	}

	// The one confirmed action of the open card: closing it out when it is open for review only, deleting it when it can be edited.
	private Task BeginConfirmation()
	{
		_confirming = true;
		_focusConfirmBack = true;
		return Task.CompletedTask;
	}

	private Task CancelConfirmation()
	{
		_confirming = false;
		_focusConfirmOpener = true;
		return Task.CompletedTask;
	}

	// Required-field messages stay hidden until the first save attempt, then follow the fields so they clear as soon as the value is fixed.
	private string? NameError => _attempted ? _form.NameError : null;

	private string? DescriptionError => _attempted ? _form.DescriptionError : null;

	private string? CategoryError => _attempted ? _form.CategoryError : null;

	private string? WorkerError => _attempted ? _form.WorkerError : null;

	private string? AttemptLimitError => _attempted ? _form.AttemptLimitError : null;

	private async Task SaveAsync()
	{
		_attempted = true;
		if (!_form.IsValid)
		{
			return;
		}

		CardInput input = _form.ToInput();

		await RunAsync(() => SaveCardAsync(input));
	}

	// Retrying carries out what the dialog shows. Edits the person has not saved are saved first, so that nothing typed is lost and the card starts again at work with the changed task.
	private async Task RetryAsync()
	{
		if (_card is null)
		{
			return;
		}

		int id = _card.Id;
		bool edited = _form.DiffersFrom(_card);

		if (edited)
		{
			_attempted = true;
			if (!_form.IsValid)
			{
				return;
			}
		}

		CardInput input = _form.ToInput();

		await RunAsync(async () =>
		{
			if (edited)
			{
				ServiceResult saved = await SaveCardAsync(input);
				if (!saved.Succeeded)
				{
					return saved;
				}
			}

			return await CardService.RetryAsync(id, _disposeCts.Token);
		});
	}

	private async Task<ServiceResult> SaveCardAsync(CardInput input)
	{
		if (_card is null)
		{
			ServiceResult<WorkCard> created = await CardService.CreateAsync(input, _disposeCts.Token);
			return created.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(created.Error ?? "The card could not be saved.");
		}

		ServiceResult<WorkCard> updated = await CardService.UpdateAsync(_card.Id, input, _disposeCts.Token);
		return updated.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(updated.Error ?? "The card could not be saved.");
	}

	private async Task DeleteAsync()
	{
		if (_card is null)
		{
			return;
		}

		int id = _card.Id;

		await RunAsync(() => CardService.DeleteAsync(id, _disposeCts.Token));
	}

	private async Task CloseCardAsync()
	{
		if (_card is null)
		{
			return;
		}

		int id = _card.Id;

		await RunAsync(() => CardService.CloseAsync(id, _disposeCts.Token));
	}

	// A refusal can mean the card changed while the dialog was open. Read it again so the dialog follows the database: a card an agent has picked up, or one that was closed, turns read-only with its real lane, and a card that no longer exists can only be dismissed. A card still in the Queue keeps the person's unsaved text, because the refusal was about the text, but takes on the stored card's state, so that a card someone else retried no longer offers a retry.
	private async Task FollowCardAsync()
	{
		if (_card is null)
		{
			return;
		}

		ServiceResult<WorkCard> current = await CardService.GetAsync(_card.Id, _disposeCts.Token);

		if (current.Value is not WorkCard stored)
		{
			_mode = CardEditorMode.Gone;
		}
		else if (!CardLifecycleRules.CanEdit(stored.Lane))
		{
			Show(stored);
		}
		else
		{
			_card = stored;
		}

		await LoadHistoryAsync();
	}

	private async Task RunAsync(Func<Task<ServiceResult>> operation)
	{
		_busy = true;
		_error = null;

		try
		{
			ServiceResult result = await operation();

			if (!result.Succeeded)
			{
				// The service is authoritative: show its refusal, follow the card to where it really is, and reload so the board shows the same.
				_error = result.Error;
				_confirming = false;
				await FollowCardAsync();
				await Changed.InvokeAsync();
				return;
			}

			_visible = false;
			await Changed.InvokeAsync();
		}
		catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
		{
		}
		finally
		{
			_busy = false;
		}
	}
}
