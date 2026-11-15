using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Pyxie.Web.Components.Settings;

/// <summary>The footer of an editor dialog: Cancel and Save, and for an item that already exists a Delete that asks for confirmation in place. Confirming puts Cancel where Delete was, so a second click on the same spot backs out instead of deleting. The keyboard focus follows: Delete hands it to that Cancel, and Delete gets it back when the confirmation ends and the dialog stays.</summary>
public partial class EditorFooter : ComponentBase
{
	private Button? _delete;
	private Button? _cancelDelete;
	private bool _confirmingDelete;
	private bool _wasVisible;

	// Where the focus goes once the next render has put the button there: the button that had it was replaced, which would leave it nowhere.
	private bool _focusCancelDelete;
	private bool _focusDelete;

	/// <summary>Gets or sets the name of the item being edited, which the labels of the confirmation buttons carry.</summary>
	[Parameter]
	public string Subject { get; set; } = string.Empty;

	/// <summary>Gets or sets a value indicating whether the dialog is shown. A dialog that is shown again starts without a pending confirmation.</summary>
	[Parameter]
	public bool Visible { get; set; }

	/// <summary>Gets or sets a value indicating whether the item exists and can be deleted, which a new item cannot.</summary>
	[Parameter]
	public bool Deletable { get; set; }

	/// <summary>Gets or sets a value indicating whether a save or a delete is running, which disables every button.</summary>
	[Parameter]
	public bool Busy { get; set; }

	/// <summary>Gets or sets the callback raised when Save is pressed.</summary>
	[Parameter]
	public EventCallback SaveRequested { get; set; }

	/// <summary>Gets or sets the callback raised when Cancel is pressed.</summary>
	[Parameter]
	public EventCallback CancelRequested { get; set; }

	/// <summary>Gets or sets the callback raised when deleting was confirmed. The footer waits for it to finish and then asks for no more confirmation, so a refused delete leaves the dialog as it was.</summary>
	[Parameter]
	public EventCallback DeleteRequested { get; set; }

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		if (Visible && !_wasVisible)
		{
			_confirmingDelete = false;
		}

		_wasVisible = Visible;
	}

	/// <inheritdoc/>
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		try
		{
			// Cancel is the safe button to land on: it stands where Delete was, so pressing the same key again backs out instead of deleting.
			if (_focusCancelDelete && _cancelDelete is not null)
			{
				_focusCancelDelete = false;
				await _cancelDelete.ElementRef.FocusAsync();
			}
			else if (_focusDelete)
			{
				_focusDelete = false;

				// A delete that went through closes the dialog, and there is nothing in it to focus then.
				if (Visible && _delete is not null)
				{
					await _delete.ElementRef.FocusAsync();
				}
			}
		}
		catch (JSDisconnectedException)
		{
		}
	}

	private void BeginDelete()
	{
		_confirmingDelete = true;
		_focusCancelDelete = true;
	}

	private void CancelDelete()
	{
		_confirmingDelete = false;
		_focusDelete = true;
	}

	private async Task ConfirmDeleteAsync()
	{
		await DeleteRequested.InvokeAsync();
		_confirmingDelete = false;
		_focusDelete = true;
	}
}
