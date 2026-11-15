using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A component that reports each time an instance of it is initialized, so a test can tell content that was created again from content that was only rendered again.</summary>
internal sealed class InitializationProbe : ComponentBase
{
	/// <summary>Gets or sets what is called when an instance is initialized.</summary>
	[Parameter]
	public required Action Initialized { get; set; }

	/// <summary>Gets or sets the text the probe renders.</summary>
	[Parameter]
	public string? Text { get; set; }

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		Initialized();
	}

	/// <inheritdoc/>
	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.AddContent(0, Text);
	}
}
