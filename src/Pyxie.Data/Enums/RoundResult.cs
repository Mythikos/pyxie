namespace Pyxie.Data.Enums;

/// <summary>What the pipeline reports when a round finishes. <see cref="Services.Rules.PipelineRules.OutcomeOf"/> turns it into the round's <see cref="RoundOutcome"/>, and <see cref="Services.Rules.PipelineRules.Next"/> decides what follows.</summary>
public enum RoundResult
{
	/// <summary>The work was done, the gates passed, the reviewer approved, the merge landed, or the merge agent resolved the conflicts.</summary>
	Succeeded = 0,

	/// <summary>A gate failed, the reviewer rejected the work, or the merge conflicted. Not valid for a work or resolve round.</summary>
	Failed = 1,

	/// <summary>The target branch had moved on and was merged into the card's branch, so the gates must check the combination. Valid for merge rounds only.</summary>
	Outdated = 2,

	/// <summary>Something broke that another attempt may get past, for example a harness crash, a timeout, or a missing result file. Uses an attempt.</summary>
	Errored = 3,

	/// <summary>The round cannot run until a person fixes something, for example a command that does not start or an agent that needs the task clarified. Stops the card at once.</summary>
	Blocked = 4,
}
