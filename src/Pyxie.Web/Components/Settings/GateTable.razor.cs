using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Components.Settings;

/// <summary>Lists the gates in the order they run. A gate moves with the arrow keys on its handle or by dragging the handle to another gate, switches on and off at once, and is added, edited, and deleted through the editor.</summary>
public partial class GateTable : ComponentBase, IDisposable
{
	private const string ArrowUp = "ArrowUp";
	private const string ArrowDown = "ArrowDown";

	// Scripts the browser runs for its own events on the list. Blazor cannot cancel one key and let the others through, and in Chrome a drop never landed when the rows only had @ondragover:preventDefault, which has no handler to go with it.
	private const string KeepArrowKeysOnHandles = "if (event.target.closest('.gate-handle') && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) { event.preventDefault(); }";
	private const string AcceptDragsOverGates = "if (event.target.closest('.gate-row')) { event.preventDefault(); }";

	private readonly CancellationTokenSource _cts = new();

	// The handles by gate, to put the focus back on the one that was moved: a moved row is a moved element in the browser, which loses the focus.
	private readonly Dictionary<int, ElementReference> _handles = [];
	private IReadOnlyList<Gate> _gates = [];
	private Gate? _editing;
	private bool _editorVisible;
	private bool _busy;
	private string? _error;
	private int? _draggingId;
	private int? _dropIndex;
	private int? _focusGateId;

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
	protected override async Task OnInitializedAsync()
	{
		await LoadAsync();
	}

	/// <inheritdoc/>
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_focusGateId is not int id)
		{
			return;
		}

		_focusGateId = null;

		if (!_handles.TryGetValue(id, out ElementReference handle))
		{
			return;
		}

		try
		{
			await handle.FocusAsync();
		}
		catch (JSDisconnectedException)
		{
		}
	}

	private static string CommandLine(Gate gate)
	{
		return gate.Arguments.Length == 0 ? gate.Command : $"{gate.Command} {gate.Arguments}";
	}

	// A disabled gate is muted, and while a gate is dragged the row it would land on shows on which side of that row it would stand.
	private string RowClass(Gate gate, int index)
	{
		string classes = gate.IsEnabled ? "gate-row" : "gate-row gate-row-disabled";

		if (_draggingId is int dragging && _dropIndex == index && IndexOf(dragging) is int from && from >= 0 && from != index)
		{
			classes += from > index ? " gate-drop-above" : " gate-drop-below";
		}

		return classes;
	}

	private int IndexOf(int gateId)
	{
		for (int index = 0; index < _gates.Count; index++)
		{
			if (_gates[index].Id == gateId)
			{
				return index;
			}
		}

		return -1;
	}

	private async Task LoadAsync()
	{
		try
		{
			_gates = await GateService.ListAsync(_cts.Token);
			foreach (int id in _handles.Keys.Where(id => _gates.All(gate => gate.Id != id)).ToList())
			{
				_handles.Remove(id);
			}
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

	private void BeginEdit(Gate gate)
	{
		_error = null;
		_editing = gate;
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

	// The arrow keys on a gate's handle move the gate one place, and the focus stays on its handle. Nothing else is handled, so Tab leaves the handle as it does any button.
	private async Task HandleKeyAsync(Gate gate, int index, KeyboardEventArgs args)
	{
		int? position = args.Key switch
		{
			ArrowUp when index > 0 => index - 1,
			ArrowDown when index < _gates.Count - 1 => index + 1,
			_ => null,
		};

		if (_busy || position is not int target)
		{
			return;
		}

		await MoveAsync(gate, target);
		_focusGateId = gate.Id;
	}

	private void BeginDrag(Gate gate)
	{
		_draggingId = gate.Id;
	}

	private void EndDrag()
	{
		_draggingId = null;
		_dropIndex = null;
	}

	private void DragEnter(int index)
	{
		if (_draggingId is not null)
		{
			_dropIndex = index;
		}
	}

	// Dropping a gate on another moves it to that place in the list as shown.
	private async Task DropAsync(int index)
	{
		int? dragging = _draggingId;
		EndDrag();

		if (_busy || dragging is not int id)
		{
			return;
		}

		int from = IndexOf(id);
		if (from < 0 || from == index)
		{
			return;
		}

		await MoveAsync(_gates[from], index);
	}

	// The position is the row's place in the list as shown, not its stored sort order: stored orders can have gaps, and the service treats the position as an index.
	private async Task MoveAsync(Gate gate, int position)
	{
		await RunAsync(() => GateService.MoveAsync(gate.Id, position, _cts.Token));
	}

	// The switch saves at once, carrying every other field of the gate unchanged.
	private async Task SetEnabledAsync(Gate gate, bool isEnabled)
	{
		GateFormModel model = GateFormModel.FromGate(gate);
		model.IsEnabled = isEnabled;
		GateInput input = model.ToInput();

		await RunAsync(async () =>
		{
			ServiceResult<Gate> result = await GateService.UpdateAsync(gate.Id, input, _cts.Token);

			return result.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(result.Error ?? "The gate could not be saved.");
		});
	}

	// Whatever the outcome, the list is read again so the table shows what the database holds; a refusal shows the service's message.
	private async Task RunAsync(Func<Task<ServiceResult>> operation)
	{
		_busy = true;
		_error = null;

		try
		{
			ServiceResult result = await operation();
			_error = result.Succeeded ? null : result.Error;
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
