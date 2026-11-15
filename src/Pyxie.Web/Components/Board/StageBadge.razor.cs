using Microsoft.AspNetCore.Components;

using Pyxie.Data.Enums;

namespace Pyxie.Web.Components.Board;

/// <summary>The badge a card shows while a round runs, naming the stage: Work and Review in green with a dot, Resolve in pink, and Merging in the dark coat with Pyxie's mark, since only she merges. The gate stage has no badge, because the lane already says Gates.</summary>
public partial class StageBadge : ComponentBase
{
	/// <summary>Gets or sets the kind of the round that is running.</summary>
	[Parameter]
	public RoundKind Kind { get; set; }
}
