using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Settings;

/// <summary>One sign-in agents' sandboxes are given, with whether it is on this machine. When it is not, the row says how to put it there, which is the only place the page does.</summary>
public partial class SignInRow : ComponentBase
{
	/// <summary>Gets or sets the name of the sign-in, such as <c>Claude Code token</c>.</summary>
	[Parameter]
	[EditorRequired]
	public required string Name { get; set; }

	/// <summary>Gets or sets a value indicating whether the sign-in was found.</summary>
	[Parameter]
	public bool Found { get; set; }

	/// <summary>Gets or sets what to do about a sign-in that was not found, with the command and the place Pyxie looks.</summary>
	[Parameter]
	public string Fix { get; set; } = string.Empty;
}
