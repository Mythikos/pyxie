using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Web.Components.Settings;

/// <summary>Shows the categories as chips in their colors. A chip's name opens an inline editor for the name and the color, its remove button deletes the category, and the form after the chips adds one in the color chosen there.</summary>
public partial class CategoryList : IDisposable
{
	private readonly CancellationTokenSource _cts = new();

	// The chips' names by category, so the focus can go back to the chip that was edited.
	private readonly Dictionary<int, ElementReference> _chips = [];
	private IReadOnlyList<Category> _categories = [];
	private Blazorise.TextInput? _editInput;
	private Blazorise.TextInput? _newInput;
	private string _newName = string.Empty;
	private HexColor _newColor = CategoryColors.Neutral;

	// Set by an add that went through, until the person types into the field or chooses a color again. Until then the cleared form stands for the category just added, so a submit that repeats that add, such as the second click of a double click or one the browser sent before it showed the form cleared, asks for nothing instead of for a blank name.
	private bool _justAdded;
	private string _editName = string.Empty;
	private HexColor _editColor = CategoryColors.Neutral;
	private Category? _editing;
	private int? _editingId => _editing?.Id;
	private bool _busy;
	private string? _error;

	// Where the focus goes once the next render has put it there: a control that has the focus and goes away, such as a chip that turns into its editor, would leave the focus nowhere.
	private int? _focusChipId;
	private bool _focusEditInput;
	private bool _focusNewInput;

	internal bool IsEditing => _editingId.HasValue || _newName.Length > 0 || _newColor != CategoryColors.Neutral || _busy;

	/// <summary>Gets or sets the category service, supplied by the framework.</summary>
	[Inject]
	public required ICategoryService CategoryService { get; set; }

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
		try
		{
			if (_focusEditInput && _editInput is not null)
			{
				_focusEditInput = false;
				await _editInput.Focus();
			}

			if (_focusChipId is int id && _chips.TryGetValue(id, out ElementReference chip))
			{
				_focusChipId = null;
				await chip.FocusAsync();
			}

			if (_focusNewInput && _newInput is not null)
			{
				_focusNewInput = false;
				await _newInput.Focus();
			}
		}
		catch (JSDisconnectedException)
		{
		}
	}

	private async Task LoadAsync()
	{
		try
		{
			_categories = await CategoryService.ListAsync(_cts.Token);
			if (_editing is Category edited && _categories.All(category => category.Id != edited.Id))
			{
				_editing = null;
			}

			foreach (int id in _chips.Keys.Where(id => _categories.All(category => category.Id != id)).ToList())
			{
				_chips.Remove(id);
			}
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
	}

	private void BeginEdit(Category category)
	{
		_error = null;
		_editing = category;
		_editName = category.Name;
		_editColor = category.Color;
		_focusEditInput = true;
	}

	private void CancelEdit()
	{
		_focusChipId = _editingId;
		_editing = null;
		_error = null;
	}

	private void OnNewNameChanged(string value)
	{
		_newName = value;
		_justAdded = false;
	}

	private void OnNewColorChanged(HexColor color)
	{
		_newColor = color;
		_justAdded = false;
	}

	// The name and the color are what was submitted, read before the write, so a color chosen while the add runs is not the one written. A refusal keeps both in the form.
	private async Task AddAsync()
	{
		if (_justAdded)
		{
			return;
		}

		string name = _newName.Trim();
		HexColor color = _newColor;

		await RunAsync(async () =>
		{
			ServiceResult<Category> result = await CategoryService.CreateAsync(name, color, _cts.Token);
			if (result.Succeeded)
			{
				_newName = string.Empty;
				_newColor = CategoryColors.Neutral;
				_justAdded = true;
			}

			return Outcome(result, "The category could not be added.");
		});
	}

	// Take both fields from the same submitted draft and apply them together against the definition the editor opened on.
	private async Task SaveEditAsync()
	{
		if (_editing is not Category original)
		{
			return;
		}

		string name = _editName.Trim();
		HexColor color = _editColor;
		await RunAsync(async () =>
		{
			if (name != original.Name || color != original.Color)
			{
				ServiceResult<Category> saved = await CategoryService.UpdateIfCurrentAsync(original.Id, name == original.Name ? null : name, color == original.Color ? null : color, SettingsRevision.Of(original), _cts.Token);
				if (!saved.Succeeded)
				{
					return Outcome(saved, "The category could not be saved.");
				}
			}

			_editing = null;
			_focusChipId = original.Id;

			return ServiceResult.Success();
		});
	}

	// A category that cards use is refused by the service, and its message shows above the chips. A chip's remove button still works until the browser shows the chip gone, so a category the list no longer shows was deleted already. Once the browser shows it gone, the next chip has slid under the pointer, so a press the browser counts as part of a double click deletes nothing.
	private async Task DeleteAsync(MouseEventArgs press, Category category)
	{
		if (press.Detail > 1 || _categories.All(listed => listed.Id != category.Id))
		{
			return;
		}

		await RunAsync(async () =>
		{
			ServiceResult result = await CategoryService.DeleteIfCurrentAsync(category.Id, SettingsRevision.Of(category), _cts.Token);
			_focusNewInput = result.Succeeded;

			return result;
		});
	}

	private static ServiceResult Outcome(ServiceResult<Category> result, string fallback)
	{
		return result.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(result.Error ?? fallback);
	}

	// One write at a time: the panel's controls are plain elements, whose disabled state only the browser keeps. Whatever the outcome, the list is read again so the chips show what the database holds.
	private async Task RunAsync(Func<Task<ServiceResult>> operation)
	{
		using var lease = AcquireProject(out _error);
		if (lease is null)
		{
			return;
		}

		if (_busy)
		{
			return;
		}

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
