using Pyxie.Data.Entities;
using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>A round the pipeline has opened and now owns: what to run, for which card, with what context. The round stays open until its result is recorded.</summary>
/// <param name="CardId">The card identifier.</param>
/// <param name="CardName">The card's name, for commit and merge messages and the agent's <see cref="AgentArgumentTokens.CardName"/> token.</param>
/// <param name="RoundId">The round identifier, which the pipeline passes back to <see cref="ICardPipelineService.RecordResultAsync"/>.</param>
/// <param name="Sequence">The one-based position of the round in the card's history.</param>
/// <param name="Kind">The stage to run.</param>
/// <param name="Attempt">The attempt of the card's run the round belongs to.</param>
/// <param name="Agent">The agent to invoke, as copied when the round opened; <see langword="null"/> for gate and merge rounds.</param>
/// <param name="Briefing">The context to give the agent; <see langword="null"/> for gate and merge rounds.</param>
public sealed record PipelineWork(int CardId, string CardName, int RoundId, int Sequence, RoundKind Kind, int Attempt, AgentSnapshot? Agent, string? Briefing);
