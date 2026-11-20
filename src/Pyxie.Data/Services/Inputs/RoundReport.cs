using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.Inputs;

/// <summary>What the pipeline records when a round finishes.</summary>
/// <param name="Note">The short reason shown in history, at most <see cref="Limits.PyxieLimits.MaxRoundNoteLength"/> characters. Required for failed, rejected, and errored rounds.</param>
/// <param name="Report">What the round produced, at most <see cref="Limits.PyxieLimits.MaxRoundReportLength"/> characters: the worker's handoff summary, the gate output, the reviewer's findings, or the merge agent's handoff on its resolution. Later briefings quote it, so the next agent sees the exact output.</param>
public sealed record RoundReport(string? Note, string? Report)
{
	/// <summary>Gets durable media to associate with the round in its finalization transaction.</summary>
	public IReadOnlyList<MediaFile> Media { get; init; } = [];
}
