namespace Pyxie.Web.Components.Board;

/// <summary>The color a round shows in while it runs: the outline of its square in the round trail and, where a stage has one, its badge.</summary>
public enum RoundStageTone
{
	/// <summary>Green: an agent works or reviews.</summary>
	Work,

	/// <summary>Amber: the gates run.</summary>
	Gate,

	/// <summary>Pink: the merge agent resolves a conflict.</summary>
	Resolve,

	/// <summary>The dark coat: Pyxie merges, the one thing she does herself.</summary>
	Merge,
}
