namespace Pyxie.Data.Enums;

/// <summary>The kind of pipeline round recorded in a card's history. Each kind runs in one lane (see <see cref="Services.Rules.PipelineRules.LaneOf"/>).</summary>
public enum RoundKind
{
	/// <summary>The worker agent carries out the card in <see cref="CardLane.InProgress"/>.</summary>
	Work = 0,

	/// <summary>The automated gates check the work in <see cref="CardLane.Gates"/>. No agent takes part.</summary>
	Gate = 1,

	/// <summary>The reviewer agent reviews the work in <see cref="CardLane.Review"/>.</summary>
	Review = 2,

	/// <summary>Pyxie merges the approved work into the target branch, still in <see cref="CardLane.Review"/>. No agent takes part.</summary>
	Merge = 3,

	/// <summary>The project's merge agent resolves the conflicts of merging the target branch into the card's branch, in <see cref="CardLane.InProgress"/>: the resolution is new work, so the gates and the review check it again.</summary>
	Resolve = 4,
}
