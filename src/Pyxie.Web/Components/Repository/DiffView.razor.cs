using Microsoft.AspNetCore.Components;

using Pyxie.Web.Components.Repository.Diffs;

namespace Pyxie.Web.Components.Repository;

/// <summary>Shows a commit's diff with added, removed, and hunk lines colored.</summary>
public partial class DiffView : ComponentBase
{
	private string? _parsed;
	private IReadOnlyList<DiffBlock> _blocks = [];

	/// <summary>Gets or sets the unified diff.</summary>
	[Parameter]
	public required string Patch { get; set; }

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		if (ReferenceEquals(_parsed, Patch))
		{
			return;
		}

		_parsed = Patch;
		_blocks = DiffParser.Parse(Patch);
	}

	private static string ClassOf(DiffBlockKind kind) => kind switch
	{
		DiffBlockKind.Header => "diff-header",
		DiffBlockKind.Hunk => "diff-hunk",
		DiffBlockKind.Added => "diff-added",
		DiffBlockKind.Removed => "diff-removed",
		DiffBlockKind.Note => "diff-note",
		_ => "diff-context",
	};
}
