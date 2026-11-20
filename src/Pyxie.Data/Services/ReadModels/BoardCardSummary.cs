using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.ReadModels;

/// <summary>A board card with aggregate history, suitable for paginated listings.</summary>
/// <param name="Card">The card with category and agent references.</param>
/// <param name="History">The summary of all its rounds.</param>
public sealed record BoardCardSummary(WorkCard Card, CardHistorySummary History);
