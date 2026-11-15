namespace Pyxie.Web.Components.Board;

/// <summary>How a round's outcome is toned, in the board's round trail and in card history, so a glance separates what went well from what went wrong.</summary>
public enum RoundOutcomeTone
{
	/// <summary>No tone: the round is still running.</summary>
	None,

	/// <summary>The stage did its job: completed, passed, approved, resolved, or merged.</summary>
	Success,

	/// <summary>The stage failed or could not run: failed, rejected, conflicted, errored, or blocked.</summary>
	Danger,

	/// <summary>The stage was made stale without being at fault: outdated, because the target branch moved on.</summary>
	Warning,

	/// <summary>The stage did not finish or did not apply: skipped, or interrupted by the pipeline stopping.</summary>
	Muted,
}
