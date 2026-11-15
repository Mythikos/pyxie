using Pyxie.Data.Enums;

namespace Pyxie.Data.Entities;

/// <summary>A unit of work on the board.</summary>
public class WorkCard
{
	private Category? _category;

	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the card title.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the description. Services require it to be non-blank.</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>Gets or sets the lane the card is in.</summary>
	public CardLane Lane { get; set; }

	/// <summary>Gets or sets the zero-based position within <see cref="Lane"/>.</summary>
	public int SortOrder { get; set; }

	/// <summary>Gets or sets whether the pipeline may take the card. Only a Queue card can be in <see cref="CardState.Error"/>.</summary>
	public CardState State { get; set; }

	/// <summary>Gets or sets how many attempts the card gets in each run before the pipeline stops it (see <c>docs/pipeline.md</c>).</summary>
	public int AttemptLimit { get; set; } = Limits.PyxieLimits.DefaultAttemptLimit;

	/// <summary>Gets or sets the attempt the card is on in its current run: 1 once the pipeline claims it from the Queue, 0 before that and after a retry.</summary>
	public int Attempt { get; set; }

	/// <summary>Gets or sets the stage the pipeline resumes at when it next claims the card from the Queue, or <see langword="null"/> to start at work. Set when the pipeline stops the card; cleared when a person edits it.</summary>
	public RoundKind? ResumeRound { get; set; }

	/// <summary>Gets or sets the card's state version. Every write that changes the card's lane, state, fields, agent assignment, or rounds increments it in the same transaction; renumbering <see cref="SortOrder"/> alone does not. Pipeline claims and transitions are conditional on it, so concurrent writers never both act on one state.</summary>
	public int Version { get; set; }

	/// <summary>Gets or sets the identifier of the card category. Every card has one.</summary>
	public int CategoryId { get; set; }

	/// <summary>Gets or sets the card category.</summary>
	/// <exception cref="InvalidOperationException">The category was read from a card that was loaded without it; include <see cref="Category"/> in the query.</exception>
	public Category Category
	{
		get => _category ?? throw new InvalidOperationException($"The category of card {Id} was not loaded. Include it in the query before reading it.");
		set => _category = value;
	}

	/// <summary>Gets or sets the identifier of the worker agent. Every card that is not complete has one (the services require it); it is cleared only on complete and closed cards when the agent is deleted or loses the worker role.</summary>
	public int? WorkerAgentId { get; set; }

	/// <summary>Gets or sets the worker agent.</summary>
	public Agent? WorkerAgent { get; set; }

	/// <summary>Gets or sets the identifier of the reviewer agent, if assigned.</summary>
	public int? ReviewerAgentId { get; set; }

	/// <summary>Gets or sets the reviewer agent, if assigned.</summary>
	public Agent? ReviewerAgent { get; set; }

	/// <summary>Gets the card's pipeline rounds. Loaded only by queries that include them.</summary>
	public ICollection<CardRound> Rounds { get; } = [];

	/// <summary>Gets or sets the UTC time the card was created.</summary>
	public DateTime CreatedUtc { get; set; }

	/// <summary>Gets or sets the UTC time the card was last changed.</summary>
	public DateTime UpdatedUtc { get; set; }

	/// <summary>Gets or sets the UTC time a person closed the card out, or <see langword="null"/> while it is not closed. Set only together with <see cref="CardLane.Closed"/>.</summary>
	public DateTime? ClosedUtc { get; set; }
}
