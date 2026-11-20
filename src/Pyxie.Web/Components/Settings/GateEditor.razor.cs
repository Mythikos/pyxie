using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Components.Settings;

/// <summary>A modal form that creates a new gate or edits an existing one, and deletes an existing one. It hides itself when it is done, and whoever shows it reads the gates again then.</summary>
public partial class GateEditor : IDisposable
{
	private readonly CancellationTokenSource _cts = new();

	// The gate the form was filled from, which Save and Delete write, and whether the editor can still act.
	private readonly EditorOpening<Gate> _opening = new();
	private GateFormModel _model = GateFormModel.CreateNew();
	private bool _attempted;
	private bool _busy;
	private string? _serviceError;

	/// <summary>Gets or sets a value indicating whether the editor is shown.</summary>
	[Parameter]
	public bool Visible { get; set; }

	/// <summary>Gets or sets the callback raised when the editor is shown or hidden.</summary>
	[Parameter]
	public EventCallback<bool> VisibleChanged { get; set; }

	/// <summary>Gets or sets the gate to edit, or <see langword="null"/> to create a new gate.</summary>
	[Parameter]
	public Gate? Gate { get; set; }

	/// <summary>Gets or sets the gate service, supplied by the framework.</summary>
	[Inject]
	public required IGateService GateService { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		if (!_opening.Follow(Visible, Gate))
		{
			return;
		}

		_model = _opening.Subject is null ? GateFormModel.CreateNew() : GateFormModel.FromGate(_opening.Subject);
		_attempted = false;
		_busy = false;
		_serviceError = null;
	}

	// Save, Delete, and Cancel act only while the editor is open and nothing is running, whatever the browser shows: a click the person made while the app was busy with the last one is handled once that has ended, when the closed modal's buttons are enabled again.
	private bool CanAct => _opening.IsOpen && !_busy;

	// Field messages stay hidden until the first save attempt, then follow the fields so each clears as soon as its value is fixed.
	private string? NameError => _attempted ? _model.NameError : null;

	private string? CommandError => _attempted ? _model.CommandError : null;

	private string? ArgumentsError => _attempted ? _model.ArgumentsError : null;

	private string? TimeoutError => _attempted ? _model.TimeoutError : null;

	private async Task SaveAsync()
	{
		using var lease = AcquireProject(out _serviceError);
		if (lease is null)
		{
			return;
		}

		if (!CanAct)
		{
			return;
		}

		_attempted = true;
		_serviceError = null;
		if (!_model.IsValid)
		{
			return;
		}

		_busy = true;

		try
		{
			ServiceResult<Gate> result = _opening.Subject is not Gate subject
				? await GateService.CreateAsync(_model.ToInput(), _cts.Token)
				: await GateService.UpdateIfCurrentAsync(subject.Id, _model.ToInput(), SettingsRevision.Of(subject), _cts.Token);
			if (!result.Succeeded)
			{
				_serviceError = result.Error;
				return;
			}

			await FinishAsync();
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
		finally
		{
			_busy = false;
		}
	}

	// A refusal, such as a gate that is no longer there, shows in the dialog and leaves the editor open.
	private async Task DeleteAsync()
	{
		using var lease = AcquireProject(out _serviceError);
		if (lease is null)
		{
			return;
		}

		if (!CanAct || _opening.Subject is not Gate subject)
		{
			return;
		}

		_serviceError = null;
		_busy = true;

		try
		{
			ServiceResult result = await GateService.DeleteIfCurrentAsync(subject.Id, SettingsRevision.Of(subject), _cts.Token);
			if (!result.Succeeded)
			{
				_serviceError = result.Error;
				return;
			}

			await FinishAsync();
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task CancelAsync()
	{
		if (!CanAct)
		{
			return;
		}

		await FinishAsync();
	}

	// The modal reports a close the person made, with Escape or the backdrop. It also repeats the showing and hiding the parent asked for once its animation has ended, 150 ms later, when that may be out of date: an editor that saved within that time would be shown again, ready to save again. Only a close is passed on, and only while the editor is open, so the parent alone shows the editor; a close repeated late still closes an editor shown again within those 150 ms.
	private async Task OnModalVisibleChangedAsync(bool visible)
	{
		if (!visible && _opening.IsOpen)
		{
			await FinishAsync();
		}
	}

	// The editor has done its work or was cancelled: it acts no more until it is shown again, and asks whoever shows it to hide it.
	private async Task FinishAsync()
	{
		_opening.Finish();
		await VisibleChanged.InvokeAsync(false);
	}
}
