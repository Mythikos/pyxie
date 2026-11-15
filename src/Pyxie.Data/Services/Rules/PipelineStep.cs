using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Rules;

/// <summary>What follows a round, as decided by <see cref="PipelineRules.Next"/>.</summary>
/// <param name="Kind">Whether a round opens, the card completes, or the card stops.</param>
/// <param name="Round">The round to open for <see cref="PipelineStepKind.Open"/>; the stage to resume at after a retry for <see cref="PipelineStepKind.Stop"/>; <see langword="null"/> for <see cref="PipelineStepKind.Complete"/>.</param>
/// <param name="Attempt">The attempt the card is on afterwards.</param>
public sealed record PipelineStep(PipelineStepKind Kind, RoundKind? Round, int Attempt)
{
	/// <summary>Creates a step that opens a round.</summary>
	/// <param name="round">The round to open.</param>
	/// <param name="attempt">The attempt the round belongs to.</param>
	/// <returns>The step.</returns>
	public static PipelineStep Open(RoundKind round, int attempt)
	{
		return new PipelineStep(PipelineStepKind.Open, round, attempt);
	}

	/// <summary>Creates a step that completes the card.</summary>
	/// <param name="attempt">The attempt the card finished on.</param>
	/// <returns>The step.</returns>
	public static PipelineStep Complete(int attempt)
	{
		return new PipelineStep(PipelineStepKind.Complete, null, attempt);
	}

	/// <summary>Creates a step that stops the card in Error.</summary>
	/// <param name="resumeAt">The stage to resume at when a person retries the card.</param>
	/// <param name="attempt">The attempt the card stopped on.</param>
	/// <returns>The step.</returns>
	public static PipelineStep Stop(RoundKind resumeAt, int attempt)
	{
		return new PipelineStep(PipelineStepKind.Stop, resumeAt, attempt);
	}
}
