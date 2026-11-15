using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.ReadModels;

/// <summary>A card on the board with the summary of its history.</summary>
/// <param name="Card">The card, with its category and assigned agents.</param>
/// <param name="History">What the card's rounds add up to.</param>
/// <param name="Rounds">The card's rounds, oldest first (by sequence), without the briefing and report text; empty before the first round. The board shows them as the card's trail.</param>
public sealed record BoardCard(WorkCard Card, CardHistorySummary History, IReadOnlyList<RoundSummary> Rounds);
