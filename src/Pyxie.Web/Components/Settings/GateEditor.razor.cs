using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Components.Settings;

/// <summary>A modal form that creates a new gate or edits an existing one, and deletes an existing one. It hides itself when it is done, and whoever shows it reads the gates again then.</summary>
public partial class GateEditor : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();
	private GateFormModel _model = GateFormModel.CreateNew();
	private bool _wasVisible;
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
		if (Visible && !_wasVisible)
		{
			_model = Gate is null ? GateFormModel.CreateNew() : GateFormModel.FromGate(Gate);
			_attempted = false;
			_busy = false;
			_serviceError = null;
		}

		_wasVisible = Visible;
	}

	// Field messages stay hidden until the first save attempt, then follow the fields so each clears as soon as its value is fixed.
	private string? NameError => _attempted ? _model.NameError : null;

	private string? CommandError => _attempted ? _model.CommandError : null;

	private string? ArgumentsError => _attempted ? _model.ArgumentsError : null;

	private string? TimeoutError => _attempted ? _model.TimeoutError : null;

	private async Task SaveAsync()
	{
		_attempted = true;
		_serviceError = null;
		if (!_model.IsValid)
		{
			return;
		}

		_busy = true;

		try
		{
			ServiceResult<Gate> result = Gate is null
				? await GateService.CreateAsync(_model.ToInput(), _cts.Token)
				: await GateService.UpdateAsync(Gate.Id, _model.ToInput(), _cts.Token);
			if (!result.Succeeded)
			{
				_serviceError = result.Error;
				return;
			}

			await VisibleChanged.InvokeAsync(false);
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
		if (Gate is null)
		{
			return;
		}

		_serviceError = null;
		_busy = true;

		try
		{
			ServiceResult result = await GateService.DeleteAsync(Gate.Id, _cts.Token);
			if (!result.Succeeded)
			{
				_serviceError = result.Error;
				return;
			}

			await VisibleChanged.InvokeAsync(false);
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
		await VisibleChanged.InvokeAsync(false);
	}
}
