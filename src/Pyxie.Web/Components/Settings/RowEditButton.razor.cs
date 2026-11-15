using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Settings;

/// <summary>The icon-only button at the end of a row that opens the row's item in its editor.</summary>
public partial class RowEditButton : ComponentBase
{
	/// <summary>Gets or sets the accessible name of the button, such as <c>Edit Claude</c>.</summary>
	[Parameter]
	[EditorRequired]
	public required string Label { get; set; }

	/// <summary>Gets or sets a value indicating whether the button cannot be pressed.</summary>
	[Parameter]
	public bool Disabled { get; set; }

	/// <summary>Gets or sets the callback raised when the button is pressed.</summary>
	[Parameter]
	public EventCallback Clicked { get; set; }
}
