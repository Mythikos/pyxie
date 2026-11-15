using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Settings;

/// <summary>Text, such as an agent's command line, with the tokens Pyxie fills in marked so they stand out from the rest.</summary>
public partial class TokenizedText : ComponentBase
{
	/// <summary>Gets or sets the text to show.</summary>
	[Parameter]
	[EditorRequired]
	public required string Text { get; set; }
}
