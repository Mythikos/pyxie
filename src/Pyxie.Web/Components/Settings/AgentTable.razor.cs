using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Web.Components.Settings;

/// <summary>Lists the agents with the roles each can take, which are set in the table itself, and opens the editor to add, change, or delete one.</summary>
public partial class AgentTable : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();
	private IReadOnlyList<Agent> _agents = [];
	private Agent? _editing;
	private bool _editorVisible;
	private bool _busy;
	private string? _error;

	// The rows are keyed with this number, which grows whenever a write was refused. A checkbox or a radio a person pressed shows the new state in the browser, and when the stored state stays as it was nothing tells the browser to take it back, so the rows are created again and show what is stored.
	private int _revision;

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
	protected override async Task OnInitializedAsync()
	{
		await LoadAsync();
	}

	private static string CommandLine(Agent agent)
	{
		return agent.Arguments.Length == 0 ? agent.Command : $"{agent.Command} {agent.Arguments}";
	}

	private static AgentInput InputOf(Agent agent)
	{
		return new AgentInput(agent.Name, agent.Harness, agent.Command, agent.Arguments, agent.CanWork, agent.CanReview, agent.CanMerge);
	}

	private async Task LoadAsync()
	{
		try
		{
			_agents = await AgentService.ListAsync(_cts.Token);
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
	}

	private void BeginAdd()
	{
		_error = null;
		_editing = null;
		_editorVisible = true;
	}

	private void BeginEdit(Agent agent)
	{
		_error = null;
		_editing = agent;
		_editorVisible = true;
	}

	// Whatever closed the editor, a save, a delete, or a person giving up, the list is read again, so it shows what the database holds.
	private async Task OnEditorVisibleChangedAsync(bool visible)
	{
		_editorVisible = visible;

		if (!visible)
		{
			await LoadAsync();
		}
	}

	// Each role saves at once, carrying every other field of the agent unchanged. A refusal, such as an agent a card that is not complete still needs for that role, shows its message.
	private async Task SetWorkAsync(Agent agent, bool canWork)
	{
		await RunAsync(() => UpdateAsync(agent, InputOf(agent) with { CanWork = canWork }));
	}

	private async Task SetReviewAsync(Agent agent, bool canReview)
	{
		await RunAsync(() => UpdateAsync(agent, InputOf(agent) with { CanReview = canReview }));
	}

	// Only one agent resolves merge conflicts, so choosing one takes the role from the agent that had it, and None takes it from whoever has it.
	private async Task SetMergeAgentAsync(int? agentId)
	{
		await RunAsync(() => AgentService.SetMergeAgentAsync(agentId, _cts.Token));
	}

	private async Task<ServiceResult> UpdateAsync(Agent agent, AgentInput input)
	{
		ServiceResult<Agent> result = await AgentService.UpdateAsync(agent.Id, input, _cts.Token);

		return result.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(result.Error ?? "The agent could not be saved.");
	}

	// Whatever the outcome, the list is read again so the table shows what the database holds.
	private async Task RunAsync(Func<Task<ServiceResult>> write)
	{
		_busy = true;
		_error = null;

		try
		{
			ServiceResult result = await write();
			if (!result.Succeeded)
			{
				_error = result.Error;
				_revision++;
			}

			await LoadAsync();
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
		finally
		{
			_busy = false;
		}
	}
}
