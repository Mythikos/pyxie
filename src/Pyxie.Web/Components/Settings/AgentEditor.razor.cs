using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Components.Settings;

/// <summary>A modal form that creates a new agent or edits an existing one, and deletes an existing one. It hides itself when it is done, and whoever shows it reads the agents again then.</summary>
public partial class AgentEditor : IDisposable
{
	private readonly CancellationTokenSource _cts = new();

	// The agent the form was filled from, which Save and Delete write, and whether the editor can still act.
	private readonly EditorOpening<Agent> _opening = new();
	private AgentFormModel _model = AgentFormModel.CreateNew();
	private bool _busy;
	private string? _nameError;
	private string? _commandError;
	private string? _argumentsError;
	private string? _serviceError;

	/// <summary>Gets or sets a value indicating whether the editor is shown.</summary>
	[Parameter]
	public bool Visible { get; set; }

	/// <summary>Gets or sets the callback raised when the editor is shown or hidden.</summary>
	[Parameter]
	public EventCallback<bool> VisibleChanged { get; set; }

	/// <summary>Gets or sets the agent to edit, or <see langword="null"/> to create a new agent.</summary>
	[Parameter]
	public Agent? Agent { get; set; }

	/// <summary>Gets or sets the agent service, supplied by the framework.</summary>
	[Inject]
	public required IAgentService AgentService { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		if (!_opening.Follow(Visible, Agent))
		{
			return;
		}

		_model = _opening.Subject is null ? AgentFormModel.CreateNew() : AgentFormModel.FromAgent(_opening.Subject);
		_nameError = null;
		_commandError = null;
		_argumentsError = null;
		_serviceError = null;
		_busy = false;
	}

	// Save, Delete, and Cancel act only while the editor is open and nothing is running, whatever the browser shows: a click the person made while the app was busy with the last one is handled once that has ended, when the closed modal's buttons are enabled again.
	private bool CanAct => _opening.IsOpen && !_busy;

	private static string? CheckRequired(string value, int maxLength, string label)
	{
		string trimmed = value.Trim();
		if (trimmed.Length == 0)
		{
			return $"{label} is required.";
		}

		return trimmed.Length > maxLength ? $"{label} must be {maxLength} characters or fewer." : null;
	}

	private bool Validate()
	{
		_nameError = CheckRequired(_model.Name, PyxieLimits.MaxNameLength, "Name");
		_commandError = CheckRequired(_model.Command, PyxieLimits.MaxCommandLength, "Command");
		_argumentsError = AgentInputRules.ValidateArguments(_model.Arguments);
		return _nameError is null && _commandError is null && _argumentsError is null;
	}

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

		_serviceError = null;
		if (!Validate())
		{
			return;
		}

		_busy = true;

		try
		{
			AgentInput input = _model.ToInput();
			ServiceResult<Agent> result = _opening.Subject is not Agent subject
				? await AgentService.CreateAsync(input, _cts.Token)
				: await AgentService.UpdateIfCurrentAsync(subject.Id, input, SettingsRevision.Of(subject), _cts.Token);
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

	// A refusal, such as an agent a card still needs, shows in the dialog and leaves the agent as it was.
	private async Task DeleteAsync()
	{
		using var lease = AcquireProject(out _serviceError);
		if (lease is null)
		{
			return;
		}

		if (!CanAct || _opening.Subject is not Agent subject)
		{
			return;
		}

		_serviceError = null;
		_busy = true;

		try
		{
			ServiceResult result = await AgentService.DeleteIfCurrentAsync(subject.Id, SettingsRevision.Of(subject), _cts.Token);
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
