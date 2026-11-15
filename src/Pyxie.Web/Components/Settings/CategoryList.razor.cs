using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;

namespace Pyxie.Web.Components.Settings;

/// <summary>Shows the categories as chips in their colors. A chip's name opens an inline editor for the name and the color, its remove button deletes the category, and the form after the chips adds one.</summary>
public partial class CategoryList : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();

	// The chips' names by category, so the focus can go back to the chip that was edited.
	private readonly Dictionary<int, ElementReference> _chips = [];
	private IReadOnlyList<Category> _categories = [];
	private Blazorise.TextInput? _editInput;
	private Blazorise.TextInput? _newInput;
	private string _newName = string.Empty;
	private string _editName = string.Empty;
	private CategoryColor _editColor;
	private int? _editingId;
	private bool _busy;
	private string? _error;

	// Where the focus goes once the next render has put it there: a control that has the focus and goes away, such as a chip that turns into its editor, would leave the focus nowhere.
	private int? _focusChipId;
	private bool _focusEditInput;
	private bool _focusNewInput;

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

	private static string SwatchClass(CategoryColor color)
	{
		return $"category-swatch category-swatch-{color.ToString().ToLowerInvariant()}";
	}

	private async Task LoadAsync()
	{
		try
		{
			_categories = await CategoryService.ListAsync(_cts.Token);
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
		_editingId = category.Id;
		_editName = category.Name;
		_editColor = category.Color;
		_focusEditInput = true;
	}

	private void CancelEdit()
	{
		_focusChipId = _editingId;
		_editingId = null;
		_error = null;
	}

	private void ChooseColor(CategoryColor color)
	{
		_editColor = color;
	}

	private async Task AddAsync()
	{
		await RunAsync(async () =>
		{
			ServiceResult<Category> result = await CategoryService.CreateAsync(_newName.Trim(), _cts.Token);
			if (result.Succeeded)
			{
				_newName = string.Empty;
			}

			return Outcome(result, "The category could not be added.");
		});
	}

	// The name and the color are two writes, and only what changed is written. A refusal of either stays in the editor with what was typed, and the chip shows what is stored.
	private async Task SaveEditAsync()
	{
		if (_editingId is not int id)
		{
			return;
		}

		await RunAsync(async () =>
		{
			Category? stored = _categories.FirstOrDefault(category => category.Id == id);
			string name = _editName.Trim();

			if (stored is null || name != stored.Name)
			{
				ServiceResult<Category> renamed = await CategoryService.RenameAsync(id, name, _cts.Token);
				if (!renamed.Succeeded)
				{
					return Outcome(renamed, "The category could not be renamed.");
				}
			}

			if (stored is null || _editColor != stored.Color)
			{
				ServiceResult<Category> colored = await CategoryService.SetColorAsync(id, _editColor, _cts.Token);
				if (!colored.Succeeded)
				{
					return Outcome(colored, "The color could not be changed.");
				}
			}

			_editingId = null;
			_focusChipId = id;

			return ServiceResult.Success();
		});
	}

	// A category that cards use is refused by the service, and its message shows above the chips.
	private async Task DeleteAsync(Category category)
	{
		await RunAsync(async () =>
		{
			ServiceResult result = await CategoryService.DeleteAsync(category.Id, _cts.Token);
			_focusNewInput = result.Succeeded;

			return result;
		});
	}

	private static ServiceResult Outcome(ServiceResult<Category> result, string fallback)
	{
		return result.Succeeded ? ServiceResult.Success() : ServiceResult.Failure(result.Error ?? fallback);
	}

	// Whatever the outcome, the list is read again so the chips show what the database holds.
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
