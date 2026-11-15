using Microsoft.AspNetCore.Components;

namespace Pyxie.Web.Components.Brand;

/// <summary>Pyxie's mark, drawn from the SVGs in wwwroot/brand exactly as designed. The design allows it in four places only: the header, the pipeline status, Pyxie's own work (the Merging badge and her merge commits), and the favicon.</summary>
public partial class PyxieMark : ComponentBase
{
	/// <summary>Gets or sets which drawing to show.</summary>
	[Parameter]
	public PyxieMarkDrawing Drawing { get; set; } = PyxieMarkDrawing.Awake;

	/// <summary>Gets or sets the width and height in pixels.</summary>
	[Parameter]
	public int Size { get; set; } = 32;

	/// <summary>Gets or sets the text alternative; leave it unset where the mark sits next to text that already says what it means, so screen readers skip it.</summary>
	[Parameter]
	public string? Label { get; set; }

	private string Source => Assets[Drawing switch
	{
		PyxieMarkDrawing.Asleep => "brand/pyxie-mark-asleep.svg",
		PyxieMarkDrawing.Small => "brand/pyxie-mark-small.svg",
		_ => "brand/pyxie-mark.svg",
	}];
}
