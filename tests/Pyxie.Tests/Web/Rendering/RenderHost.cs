using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A root component that renders whatever fragment it is given, so a test can create a component under test and keep a reference to the instance.</summary>
internal sealed class RenderHost : ComponentBase
{
	/// <summary>Gets or sets the fragment to render.</summary>
	[Parameter]
	public RenderFragment? Content { get; set; }

	/// <inheritdoc/>
	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.AddContent(0, Content);
	}
}
