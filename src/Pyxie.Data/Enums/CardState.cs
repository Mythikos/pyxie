namespace Pyxie.Data.Enums;

/// <summary>Whether the pipeline may take a card. Only Queue cards can be in <see cref="Error"/> (see <c>docs/pipeline.md</c>).</summary>
public enum CardState
{
	/// <summary>The pipeline takes the card when it reaches the top of the Queue, or carries on with it in an active lane.</summary>
	Ready = 0,

	/// <summary>The pipeline stopped the card: it used its last attempt or was blocked. It waits at the top of the Queue until a person retries it.</summary>
	Error = 1,
}
