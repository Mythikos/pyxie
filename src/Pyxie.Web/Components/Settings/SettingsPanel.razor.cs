using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Settings;

/// <summary>The white panel one section of the Settings page lives in: its heading, the actions that belong to the whole section, and the section's content. The panel is the target of the section nav's anchor.</summary>
public partial class SettingsPanel : ComponentBase
{
	/// <summary>Gets or sets the section the panel shows, which gives it its anchor and its heading.</summary>
	[Parameter]
	[EditorRequired]
	public required SettingsSection Section { get; set; }

	/// <summary>Gets or sets the actions on the right of the heading, such as adding an item.</summary>
	[Parameter]
	public RenderFragment? Actions { get; set; }

	/// <summary>Gets or sets the content below the heading.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>Gets or sets a value indicating whether the content runs from edge to edge below a padded heading, as a table or a list does. Otherwise the whole panel is padded and its parts are spaced apart.</summary>
	[Parameter]
	public bool Flush { get; set; }

	/// <summary>Gets or sets the attributes the panel's element is given as they are, such as a style that sets the spacing of a padded panel.</summary>
	[Parameter(CaptureUnmatchedValues = true)]
	public Dictionary<string, object>? AdditionalAttributes { get; set; }

	private string HeadingId => $"{Section.Id}-heading";

	private string PanelClass => Flush ? "pyx-panel settings-panel settings-panel-flush" : "pyx-panel settings-panel settings-panel-padded";
}
