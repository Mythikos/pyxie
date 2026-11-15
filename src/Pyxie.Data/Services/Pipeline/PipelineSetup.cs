using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>What the open project's settings contribute to a card's run, read in the same transaction that advances the card: whether any gate is enabled, and the project's merge agent.</summary>
/// <param name="HasGates">Whether at least one gate is enabled.</param>
/// <param name="Merger">The agent that resolves merge conflicts, or <see langword="null"/> when the project has none and conflicts go back to the card's worker.</param>
internal sealed record PipelineSetup(bool HasGates, Agent? Merger);
