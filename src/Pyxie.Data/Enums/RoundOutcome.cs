namespace Pyxie.Data.Enums;

/// <summary>How a pipeline round ended. A round that is still running has no outcome.</summary>
public enum RoundOutcome
{
	/// <summary>The worker finished; the gates check the work next.</summary>
	Completed = 0,

	/// <summary>The gates passed.</summary>
	Passed = 1,

	/// <summary>A gate failed; the card went back for another work round with the failure as the reason.</summary>
	Failed = 2,

	/// <summary>The reviewer approved; Pyxie merges the work next.</summary>
	Approved = 3,

	/// <summary>The reviewer rejected; the card went back for another work round with the findings as the reason.</summary>
	Rejected = 4,

	/// <summary>The work was merged into the target branch; the card is complete.</summary>
	Merged = 5,

	/// <summary>Merging the target branch into the card's branch conflicted; the card went to the project's merge agent to resolve it, or back to its worker when the project has none.</summary>
	Conflicted = 6,

	/// <summary>The target branch had moved on and was merged into the card's branch; the gates check the combination again.</summary>
	Outdated = 7,

	/// <summary>The merge agent resolved the conflicts and the merge came back as one merge commit; the gates and the review check it next.</summary>
	Resolved = 8,

	/// <summary>The stage did not apply: no gate was enabled, or the card has no reviewer. The pipeline moved on as if it had passed.</summary>
	Skipped = 9,

	/// <summary>Something broke that another attempt may get past, for example a harness crash or a timeout.</summary>
	Errored = 10,

	/// <summary>The round could not run until a person fixes something. The card stopped in Error.</summary>
	Blocked = 11,

	/// <summary>Pyxie stopped, or the pipeline was stopped, while the round was running. The pipeline resumes the card at the same stage.</summary>
	Interrupted = 12,
}
