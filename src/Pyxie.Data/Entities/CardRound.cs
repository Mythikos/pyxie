using Pyxie.Data.Enums;

namespace Pyxie.Data.Entities;

/// <summary>One pass of a card through a pipeline stage: a work, gate, review, merge, or resolve round. Rounds are append-only history; the pipeline opens one when it starts a stage and closes it with an outcome, so a card's rounds show how many times it was worked, checked, and reviewed, by what, and why it was sent back. A card has at most one open round, and only while a pipeline worker owns it.</summary>
public class CardRound
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the identifier of the card the round belongs to.</summary>
	public int CardId { get; set; }

	/// <summary>Gets or sets the one-based position of the round in the card's history, across all kinds.</summary>
	public int Sequence { get; set; }

	/// <summary>Gets or sets the stage the round ran.</summary>
	public RoundKind Kind { get; set; }

	/// <summary>Gets or sets the attempt of the card's run the round belongs to (see <see cref="WorkCard.Attempt"/>).</summary>
	public int Attempt { get; set; }

	/// <summary>Gets or sets the agent that ran the round, copied when it started. <see langword="null"/> for gate and merge rounds and for rounds that never ran (skipped or blocked).</summary>
	public AgentSnapshot? Agent { get; set; }

	/// <summary>Gets or sets the context the agent was given when the round started (see <see cref="Services.Pipeline.CardBriefingBuilder"/>). <see langword="null"/> for rounds that run no agent.</summary>
	public string? Briefing { get; set; }

	/// <summary>Gets or sets the UTC time the round started.</summary>
	public DateTime StartedUtc { get; set; }

	/// <summary>Gets or sets the UTC time the round ended, or <see langword="null"/> while it is running. Set together with <see cref="Outcome"/>.</summary>
	public DateTime? EndedUtc { get; set; }

	/// <summary>Gets or sets how the round ended, or <see langword="null"/> while it is running.</summary>
	public RoundOutcome? Outcome { get; set; }

	/// <summary>Gets or sets the short reason shown in history. Required when the round failed, was rejected, conflicted, errored, or was blocked.</summary>
	public string? Note { get; set; }

	/// <summary>Gets or sets what the round produced: the worker's handoff summary, the gate output, the reviewer's findings, the merge result, or the merge agent's handoff on its resolution. Passed on to later rounds' briefings.</summary>
	public string? Report { get; set; }
}
