namespace Pyxie.Data.Limits;

/// <summary>Owns the maximum lengths shared by the persistence model, services, and user interface.</summary>
public static class PyxieLimits
{
	/// <summary>The maximum length of a card, category, or agent name.</summary>
	public const int MaxNameLength = 100;

	/// <summary>The maximum length of a card description.</summary>
	public const int MaxDescriptionLength = 4000;

	/// <summary>The maximum length of an agent command.</summary>
	public const int MaxCommandLength = 500;

	/// <summary>The maximum length of an agent arguments string.</summary>
	public const int MaxArgumentsLength = 2000;

	/// <summary>The maximum length of a round's short history note, such as a rejection reason.</summary>
	public const int MaxRoundNoteLength = 1000;

	/// <summary>The maximum length of a round's report: a worker handoff, gate output, or review findings. The pipeline trims longer output before recording it.</summary>
	public const int MaxRoundReportLength = 100_000;

	/// <summary>The maximum length of the briefing an agent is given when a round starts.</summary>
	public const int MaxBriefingLength = 60_000;

	/// <summary>The attempt limit a new card gets.</summary>
	public const int DefaultAttemptLimit = 5;

	/// <summary>The highest attempt limit a card can have, so no card can loop for long before a person looks at it.</summary>
	public const int MaxAttemptLimit = 10;

	/// <summary>The timeout a new gate gets, in minutes.</summary>
	public const int DefaultGateTimeoutMinutes = 30;

	/// <summary>The longest timeout a gate can have, in minutes.</summary>
	public const int MaxGateTimeoutMinutes = 240;

	/// <summary>The maximum length of a repository path.</summary>
	public const int MaxPathLength = 1000;

	/// <summary>The maximum length of the folder or URL a repository was imported from.</summary>
	public const int MaxOriginLength = 2000;

	/// <summary>The maximum length of a branch name.</summary>
	public const int MaxBranchNameLength = 250;

	/// <summary>The maximum length of a project image's Dockerfile.</summary>
	public const int MaxDockerfileLength = 20_000;
}
