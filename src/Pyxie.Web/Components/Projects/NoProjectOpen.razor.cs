using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Projects;

/// <summary>What a page that belongs to a project shows while none is open: that no project is open, and the way to the Repository page, where projects are opened and imported.</summary>
public partial class NoProjectOpen : ComponentBase
{
	/// <summary>The sentence that tells a person no project is open, wherever something is unavailable for that reason.</summary>
	public const string Text = "No project is open.";
}
