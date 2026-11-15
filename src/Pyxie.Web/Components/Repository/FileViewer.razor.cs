using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Git;
using Pyxie.Web.Components.Repository.Display;

namespace Pyxie.Web.Components.Repository;

/// <summary>Shows one file: its text with line numbers, or a note that it is binary or too large to show.</summary>
public partial class FileViewer : ComponentBase
{
	private GitFileContent? _shown;
	private string _gutter = string.Empty;

	/// <summary>Gets or sets the file to show.</summary>
	[Parameter]
	public required GitFileContent Content { get; set; }

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		// The numbers are one block of text; build them once per file, not on every render.
		if (ReferenceEquals(_shown, Content))
		{
			return;
		}

		_shown = Content;
		_gutter = Content.Text is null ? string.Empty : FileText.Gutter(FileText.LineCount(Content.Text));
	}
}
