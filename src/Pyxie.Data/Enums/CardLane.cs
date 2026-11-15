namespace Pyxie.Data.Enums;

/// <summary>The lanes a card passes through, in pipeline order. The first five are the board lanes; <see cref="Closed"/> is the terminal state and is not shown on the board. People add cards to the Queue and close out Complete cards; the pipeline moves cards through every other lane (see <c>docs/pipeline.md</c>).</summary>
public enum CardLane
{
	/// <summary>Work waiting for the pipeline to claim it. New cards join at the bottom and the pipeline takes from the top.</summary>
	Queue = 0,

	/// <summary>Work being carried out by the worker agent, or a merge conflict being resolved by the merge agent.</summary>
	InProgress = 1,

	/// <summary>Work being checked by the automated gates (tests, linters, analysis) before an agent reviews it.</summary>
	Gates = 2,

	/// <summary>Work being reviewed by the reviewer agent.</summary>
	Review = 3,

	/// <summary>Approved work.</summary>
	Complete = 4,

	/// <summary>Approved work that a person has acknowledged. The card leaves the board and stays in the database as history; no lane follows it.</summary>
	Closed = 5,
}
