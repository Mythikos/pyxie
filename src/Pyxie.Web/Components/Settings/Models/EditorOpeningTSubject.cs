namespace Pyxie.Web.Components.Settings.Models;

/// <summary>An opening of an editor dialog that its parent shows through a <c>Visible</c> parameter: the item it was opened for, and whether it can still act. The dialog acts once per opening, and on what it was opened for. A closed Blazorise modal keeps its buttons, so a second click on Save that the person made while the first save ran still reaches the dialog after it has closed, and an Edit click on another row that arrives while it is open changes the parameter it was opened from: neither may make the dialog write again or write another item.</summary>
/// <typeparam name="TSubject">The kind of item the dialog edits.</typeparam>
public sealed class EditorOpening<TSubject>
	where TSubject : class
{
	private bool _wasVisible;

	/// <summary>Gets the item the dialog was opened for, or <see langword="null"/> when it was opened to add one.</summary>
	public TSubject? Subject { get; private set; }

	/// <summary>Gets a value indicating whether the dialog is open and has not finished.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Follows the dialog's parameters each time they are set. Showing the dialog opens it for the item it is shown with, an item that changes while it is shown is ignored, and hiding the dialog ends the opening.</summary>
	/// <param name="visible">Whether the dialog is shown.</param>
	/// <param name="subject">The item the dialog is shown with, or <see langword="null"/> to add one.</param>
	/// <returns><see langword="true"/> when the dialog has just been opened, so the editor fills its form from <see cref="Subject"/>.</returns>
	public bool Follow(bool visible, TSubject? subject)
	{
		bool opened = visible && !_wasVisible;
		_wasVisible = visible;

		if (opened)
		{
			Subject = subject;
			IsOpen = true;
		}
		else if (!visible)
		{
			IsOpen = false;
		}

		return opened;
	}

	/// <summary>Ends the opening once the dialog has done its work or was cancelled: it does nothing more until it is shown again.</summary>
	public void Finish()
	{
		IsOpen = false;
	}
}
