using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Components.Settings;

/// <summary>A modal form that creates a new agent or edits an existing one, and deletes an existing one. It hides itself when it is done, and whoever shows it reads the agents again then.</summary>
public partial class AgentEditor : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();
	private AgentFormModel _model = AgentFormModel.CreateNew();
	private bool _wasVisible;
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
		if (Visible && !_wasVisible)
		{
			_model = Agent is null ? AgentFormModel.CreateNew() : AgentFormModel.FromAgent(Agent);
			_nameError = null;
			_commandError = null;
			_argumentsError = null;
			_serviceError = null;
			_busy = false;
		}

		_wasVisible = Visible;
	}

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
		_serviceError = null;
		if (!Validate())
		{
			return;
		}

		_busy = true;

		try
		{
			AgentInput input = _model.ToInput();
			ServiceResult<Agent> result = Agent is null
				? await AgentService.CreateAsync(input, _cts.Token)
				: await AgentService.UpdateAsync(Agent.Id, input, _cts.Token);
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

	// A refusal, such as an agent a card still needs, shows in the dialog and leaves the agent as it was.
	private async Task DeleteAsync()
	{
		if (Agent is null)
		{
			return;
		}

		_serviceError = null;
		_busy = true;

		try
		{
			ServiceResult result = await AgentService.DeleteAsync(Agent.Id, _cts.Token);
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
