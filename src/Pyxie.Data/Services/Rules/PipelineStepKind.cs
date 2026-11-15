namespace Pyxie.Data.Services.Rules;

/// <summary>What the pipeline does with a card after a round ends.</summary>
public enum PipelineStepKind
{
	/// <summary>Open the next round, in the lane of its kind.</summary>
	Open = 0,

	/// <summary>The card's run is finished: it moves to Complete.</summary>
	Complete = 1,

	/// <summary>The card stops in Error at the top of the Queue until a person retries it.</summary>
	Stop = 2,
}
