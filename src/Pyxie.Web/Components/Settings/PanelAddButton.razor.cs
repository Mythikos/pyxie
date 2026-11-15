using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Settings;

/// <summary>The button on the right of a panel's heading that adds an item to the section, such as an agent or a gate.</summary>
public partial class PanelAddButton : ComponentBase
{
	/// <summary>Gets or sets what the button says, such as <c>Add agent</c>.</summary>
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
