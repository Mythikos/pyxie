using System.Globalization;
using System.Text;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>Builds the context an agent is given when the pipeline starts a work, review, or resolve round. The briefing is derived from the card and its recorded rounds, never kept by hand: the task, the stage and attempt, the workspace and how to report the result (see <see cref="PipelineContract"/>), the latest reason the card was sent back (its note in full and its report, so the worker sees the exact gate output, review findings, or conflicting files), the conflict a merge agent resolves, the work or the resolution to review, and one line per earlier round. A quoted report longer than 15,000 characters keeps its beginning and its end, where gate output usually puts its summary; when a review follows a resolution, which quotes four reports instead of three, the three after the resolution are held to 10,000 so that all four fit. Repository conventions are not repeated here; the harness loads them from its workspace. The result is stored on the round, so history shows what each agent knew.</summary>
public static class CardBriefingBuilder
{
	// Each quoted report is capped so a briefing with several stays under MaxBriefingLength, leaving the final truncation to cut only history; a longer one keeps this much of its beginning and the rest from its end.
	private const int MaxQuotedReportLength = 15_000;
	private const int QuotedReportHeadLength = 5_000;

	// A review of a resolution quotes four reports (the resolution, the work, the gates, an earlier rejection) where every other briefing quotes at most three. The resolution keeps the full cap, because it is what the reviewer is there for and its report carries the remerge diff; the three after it share what is left of the briefing limit.
	private const int MaxFollowingQuotedReportLength = 10_000;
	private const int FollowingQuotedReportHeadLength = 4_000;
	private const int MaxHistoryLines = 20;
	private const int MaxNoteInHistoryLine = 200;

	/// <summary>Builds the briefing for a round about to start.</summary>
	/// <param name="card">The card, with its category loaded and <see cref="WorkCard.Attempt"/> set to the attempt the round belongs to.</param>
	/// <param name="kind">The kind of round about to start: <see cref="RoundKind.Work"/>, <see cref="RoundKind.Review"/>, or <see cref="RoundKind.Resolve"/>.</param>
	/// <param name="sequence">The one-based sequence the new round will have in the card's history.</param>
	/// <param name="previousRounds">The card's earlier rounds, in any order. All of them are finished.</param>
	/// <param name="targetBranch">The branch the card's work is merged into.</param>
	/// <returns>Markdown text no longer than <see cref="PyxieLimits.MaxBriefingLength"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> runs no agent; gates and merges run commands and have no briefing.</exception>
	public static string Build(WorkCard card, RoundKind kind, int sequence, IReadOnlyCollection<CardRound> previousRounds, string targetBranch)
	{
		if (!kind.RunsAgent())
		{
			throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only work, review, and resolve rounds have a briefing.");
		}

		List<CardRound> history = [.. previousRounds.OrderBy(round => round.Sequence)];
		string branch = PipelineContract.BranchName(card.Id);
		StringBuilder text = new();

		text.Append(CultureInfo.InvariantCulture, $"# Card {card.Id}: {card.Name}").AppendLine().AppendLine();
		text.Append(CultureInfo.InvariantCulture, $"- Category: {card.Category.Name}").AppendLine();
		text.Append(CultureInfo.InvariantCulture, $"- Stage: {kind.DisplayName()} (attempt {card.Attempt} of {card.AttemptLimit}; round {sequence} of this card)").AppendLine().AppendLine();
		text.AppendLine("## Task").AppendLine();
		text.AppendLine(card.Description).AppendLine();

		// The workspace and the result format come before the history, so the final truncation can only ever cut history.
		AppendWorkspace(text, kind, branch, targetBranch);
		AppendResultFormat(text, kind);

		switch (kind)
		{
			case RoundKind.Work:
				AppendWorkContext(text, history, targetBranch);
				break;
			case RoundKind.Resolve:
				AppendResolveContext(text, history);
				break;
			default:
				AppendReviewContext(text, history, targetBranch);
				break;
		}

		AppendHistory(text, history);

		return ReportText.Truncate(text.ToString().TrimEnd(), PyxieLimits.MaxBriefingLength);
	}

	private static void AppendWorkspace(StringBuilder text, RoundKind kind, string branch, string targetBranch)
	{
		text.AppendLine("## Workspace").AppendLine();

		if (kind == RoundKind.Work)
		{
			text.Append(CultureInfo.InvariantCulture, $"You are in a sandbox, in a clone of the project on branch `{branch}`, which started from `{targetBranch}`. Make the change here. ");
			text.Append(CultureInfo.InvariantCulture, $"Pyxie commits whatever you leave when you finish and then runs the gates; once they pass and the work is approved, it merges `{branch}` into `{targetBranch}`. ");
			text.Append("Stay on this branch and do not push: the work leaves this repository only through that merge.");
		}
		else if (kind == RoundKind.Resolve)
		{
			text.Append(CultureInfo.InvariantCulture, $"You are in a sandbox, in a clone of the project on branch `{branch}`, which holds the card's work. Pyxie has started merging `{targetBranch}` into it, and the merge stopped on conflicts; `git status` lists them. ");
			text.Append(CultureInfo.InvariantCulture, $"Resolve them so the card's change and the changes on `{targetBranch}` both still hold. Keep the intent of both sides and add nothing else; if the combination breaks something outside the conflicting files, fix it as part of the merge. ");
			text.Append("Leave the resolved files in place: Pyxie commits the merge when you finish, or you can commit it yourself with `git commit --no-edit`. ");
			text.Append(CultureInfo.InvariantCulture, $"The merge must come back as one merge commit of `{branch}` and `{targetBranch}`, so do not abort it, start over, rebase, make other commits, or push. The gates and the review check the resolution next.");
		}
		else
		{
			text.Append(CultureInfo.InvariantCulture, $"The work is on branch `{branch}`, checked out here. Review what it changes compared with `{targetBranch}`, for example with `git diff {targetBranch}...HEAD`. ");
			text.Append("Do not change files or push: anything changed during a review is discarded. ");
			text.Append(CultureInfo.InvariantCulture, $"Files that steer an agent (CLAUDE.md, AGENTS.md, .claude/, .mcp.json, opencode.json) are as on `{targetBranch}` here; review the card's changes to them with git diff.");
		}

		text.AppendLine().AppendLine();
	}

	private static void AppendResultFormat(StringBuilder text, RoundKind kind)
	{
		IReadOnlyList<string> words = PipelineContract.OutcomeWords(kind);

		text.AppendLine("## When you finish").AppendLine();
		text.Append(CultureInfo.InvariantCulture, $"Write `{PipelineContract.ResultFile}` with this shape:").AppendLine().AppendLine();
		text.AppendLine("```json");
		text.Append(CultureInfo.InvariantCulture, $"{{ \"outcome\": \"{words[0]}\", \"note\": \"One line for the card's history.\", \"report\": \"The details.\" }}").AppendLine();
		text.AppendLine("```").AppendLine();

		switch (kind)
		{
			case RoundKind.Work:
				text.Append(CultureInfo.InvariantCulture, $"- `{PipelineContract.Completed}`: the task is done. In `report`, hand off to the reviewer: what changed, why, and how you checked it.").AppendLine();
				break;
			case RoundKind.Resolve:
				text.Append(CultureInfo.InvariantCulture, $"- `{PipelineContract.Resolved}`: every conflict is resolved. In `report`, hand off to the reviewer: how you resolved each conflict and why.").AppendLine();
				break;
			default:
				text.Append(CultureInfo.InvariantCulture, $"- `{PipelineContract.Approved}`: the work does what the task asks and is ready to merge.").AppendLine();
				text.Append(CultureInfo.InvariantCulture, $"- `{PipelineContract.Rejected}`: the work needs changes. List each finding in `report`; they go to the worker.").AppendLine();
				break;
		}

		string blockedExample = kind == RoundKind.Resolve ? "the card's change and the changes on the target branch cannot both hold" : "the task is unclear or access is missing";
		text.Append(CultureInfo.InvariantCulture, $"- `{PipelineContract.Errored}`: something broke that another attempt may get past. Say what in `note`.").AppendLine();
		text.Append(CultureInfo.InvariantCulture, $"- `{PipelineContract.Blocked}`: you cannot go on without a person, for example {blockedExample}. Say what you need in `note`; the card stops until someone retries it.").AppendLine();
		text.AppendLine();
	}

	// A worker needs the latest reason the card came back, and how its last attempt ended when that attempt did not finish. A merge conflict comes back to the worker only when the project has no merge agent, and the worker then merges the target branch itself. Once a round that changes the work (the worker's own, or a merge agent's resolution) has followed the conflict, the merge is done and the line that asks for it is left out, as when a person edits a card that stopped at its merge afterwards.
	private static void AppendWorkContext(StringBuilder text, List<CardRound> history, string targetBranch)
	{
		CardRound? sentBack = history.LastOrDefault(round => round.Outcome is RoundOutcome outcome && outcome.IsSendBack());
		if (sentBack is not null)
		{
			AppendQuotedRound(text, "Why this card came back", sentBack);

			bool mergedSince = history.Any(round => round.Sequence > sentBack.Sequence && round is { Kind: RoundKind.Work, Outcome: RoundOutcome.Completed } or { Kind: RoundKind.Resolve, Outcome: RoundOutcome.Resolved });
			if (sentBack.Kind == RoundKind.Merge && !mergedSince)
			{
				text.Append(CultureInfo.InvariantCulture, $"Merge `{targetBranch}` into this branch with `git merge {targetBranch}`, resolve the conflicts so the card's change and the changes on `{targetBranch}` both still hold, and finish the merge; Pyxie commits it with the rest of your work.").AppendLine().AppendLine();
			}
		}

		AppendUnfinishedAttempt(text, history, RoundKind.Work);
	}

	// A merge agent needs the conflict, which the merge round reported together with what landed on the target branch, the worker's handoff to understand the card's side, and how its last attempt ended when that attempt did not finish.
	private static void AppendResolveContext(StringBuilder text, List<CardRound> history)
	{
		CardRound? conflict = history.LastOrDefault(round => round is { Kind: RoundKind.Merge, Outcome: RoundOutcome.Conflicted });
		if (conflict is not null)
		{
			AppendQuotedRound(text, "Why the merge stopped", conflict);
		}

		CardRound? work = history.LastOrDefault(round => round is { Kind: RoundKind.Work, Outcome: RoundOutcome.Completed });
		if (work is not null)
		{
			AppendQuotedRound(text, "The card's work", work);
		}

		AppendUnfinishedAttempt(text, history, RoundKind.Resolve);
	}

	// The previous round, when it was this kind's and did not finish: its note says what to pick up.
	private static void AppendUnfinishedAttempt(StringBuilder text, List<CardRound> history, RoundKind kind)
	{
		CardRound? last = history.LastOrDefault();
		if (last is { Outcome: RoundOutcome.Errored or RoundOutcome.Blocked or RoundOutcome.Interrupted } && last.Kind == kind)
		{
			AppendQuotedRound(text, "How the previous attempt ended", last);
		}
	}

	// A reviewer needs the worker's handoff, the gate results it passed, and the previous rejection so it can check that the findings were addressed. When the card's latest change is a merge agent's resolution, that resolution is what nobody has reviewed: it leads, with the worker's handoff after it for reference.
	private static void AppendReviewContext(StringBuilder text, List<CardRound> history, string targetBranch)
	{
		CardRound? work = history.LastOrDefault(round => round is { Kind: RoundKind.Work, Outcome: RoundOutcome.Completed });
		CardRound? latestChange = history.LastOrDefault(round => round is { Kind: RoundKind.Work, Outcome: RoundOutcome.Completed } or { Kind: RoundKind.Resolve, Outcome: RoundOutcome.Resolved });
		bool afterResolution = latestChange is { Kind: RoundKind.Resolve };
		int maxReportLength = afterResolution ? MaxFollowingQuotedReportLength : MaxQuotedReportLength;
		int headLength = afterResolution ? FollowingQuotedReportHeadLength : QuotedReportHeadLength;

		if (latestChange is { Kind: RoundKind.Resolve })
		{
			text.AppendLine("## Merge to review").AppendLine();
			text.Append(CultureInfo.InvariantCulture, $"After the card's work was approved, `{targetBranch}` moved on and its changes conflicted with the card's. The merge agent resolved the conflicts in a merge commit, the last commit of this branch. ");
			text.Append(CultureInfo.InvariantCulture, $"`git show --remerge-diff HEAD` shows how each conflict was resolved compared with what git produced on its own; the resolution's report below includes it. Check that the card's change and the changes on `{targetBranch}` both still hold.").AppendLine().AppendLine();
			AppendQuotedRound(text, "The resolution", latestChange);
		}

		if (work is not null)
		{
			AppendQuotedRound(text, afterResolution ? "The card's work" : "Work to review", work, maxReportLength, headLength);
		}

		CardRound? gates = history.LastOrDefault(round => round is { Kind: RoundKind.Gate, Outcome: RoundOutcome.Passed or RoundOutcome.Skipped });
		if (gates is not null)
		{
			AppendQuotedRound(text, "Gate results", gates, maxReportLength, headLength);
		}

		CardRound? rejection = history.LastOrDefault(round => round is { Kind: RoundKind.Review, Outcome: RoundOutcome.Rejected });
		if (rejection is not null)
		{
			AppendQuotedRound(text, "Previous review findings", rejection, maxReportLength, headLength);
		}
	}

	private static void AppendQuotedRound(StringBuilder text, string heading, CardRound round, int maxReportLength = MaxQuotedReportLength, int headLength = QuotedReportHeadLength)
	{
		text.Append(CultureInfo.InvariantCulture, $"## {heading}").AppendLine().AppendLine();
		text.Append(CultureInfo.InvariantCulture, $"{Describe(round)}.").AppendLine();

		if (!string.IsNullOrWhiteSpace(round.Note))
		{
			text.AppendLine().AppendLine(round.Note);
		}

		if (!string.IsNullOrWhiteSpace(round.Report))
		{
			text.AppendLine().AppendLine(ReportText.Excerpt(round.Report, maxReportLength, headLength));
		}

		text.AppendLine();
	}

	private static void AppendHistory(StringBuilder text, List<CardRound> history)
	{
		if (history.Count == 0)
		{
			return;
		}

		text.AppendLine("## History").AppendLine();

		int omitted = Math.Max(0, history.Count - MaxHistoryLines);
		if (omitted > 0)
		{
			text.Append(CultureInfo.InvariantCulture, $"- ({omitted} earlier {(omitted == 1 ? "round" : "rounds")} omitted)").AppendLine();
		}

		foreach (CardRound round in history.Skip(omitted))
		{
			text.Append("- ").Append(Describe(round));
			if (!string.IsNullOrWhiteSpace(round.Note))
			{
				text.Append(": ").Append(ReportText.Clip(OneLine(round.Note), MaxNoteInHistoryLine));
			}

			text.AppendLine();
		}
	}

	private static string Describe(CardRound round)
	{
		string agent = round.Agent is null ? string.Empty : $" by {round.Agent.Name}";

		return string.Create(CultureInfo.InvariantCulture, $"Round {round.Sequence}, attempt {round.Attempt}, {round.Kind.DisplayName()}, {round.Outcome.DisplayName()}{agent}");
	}

	private static string OneLine(string value)
	{
		return string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
	}
}
