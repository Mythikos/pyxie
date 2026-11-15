using System.Globalization;
using System.Text;

using Pyxie.Data.Entities;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Integration;

/// <summary>Runs a merge round, in the hub and without a checkout. A card branch that already contains the target branch is integrated as a merge commit on the target branch, put in place by a compare-and-swap of the branch: one atomic step that a crash leaves either done or not started. Otherwise the target branch is merged into the card's branch with <c>merge-tree</c>: a clean merge becomes a merge commit on the card's branch and sends the combination back through the gates, and a conflict is reported with the files that conflict and what landed on the target branch since the card's branch last took it in, for whoever resolves it: the pipeline sends it to the project's merge agent, or to the worker when the project has none. Integrations run one at a time.</summary>
public sealed class CardIntegrator
{
	// What landed on the target branch is listed up to this many entries, newest first, and each card's task is quoted up to this many characters.
	private const int MaxLandedEntries = 30;
	private const int MaxQuotedDescriptionLength = 1000;

	private readonly GitClient _git;
	private readonly RepositoryLock _repositoryLock;

	/// <summary>Initializes a new instance of the <see cref="CardIntegrator"/> class.</summary>
	/// <param name="git">The git client.</param>
	/// <param name="repositoryLock">The lock that serializes integrations.</param>
	public CardIntegrator(GitClient git, RepositoryLock repositoryLock)
	{
		_git = git;
		_repositoryLock = repositoryLock;
	}

	/// <summary>Merges a card's branch into the target branch.</summary>
	/// <param name="repository">The connected repository.</param>
	/// <param name="work">The merge round.</param>
	/// <param name="describeCard">Finds the task of a card that landed on the target branch, for the report of a conflict.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>Merged, conflicted (a send-back), or outdated (back through the gates).</returns>
	/// <exception cref="GitException">git failed unexpectedly, or the card's branch is missing.</exception>
	public async Task<RoundVerdict> IntegrateAsync(Repository repository, PipelineWork work, CardDescriptionLookup describeCard, CancellationToken cancellationToken)
	{
		string hub = repository.Path;
		string target = repository.TargetBranch;
		string targetRef = $"refs/heads/{target}";
		string branch = PipelineContract.BranchName(work.CardId);

		string targetTip = await _git.RevParseAsync(hub, targetRef, cancellationToken);
		string cardTip = await CardTipAsync(hub, branch, cancellationToken);

		// The card's work is already in the target branch: it made no changes, or its merge landed just before Pyxie stopped.
		if (await _git.IsAncestorAsync(hub, cardTip, targetTip, cancellationToken))
		{
			return RoundVerdict.Succeeded($"Already merged into {target}.");
		}

		IReadOnlyList<string> conflicts;
		using (await _repositoryLock.AcquireAsync(cancellationToken))
		{
			targetTip = await _git.RevParseAsync(hub, targetRef, cancellationToken);
			cardTip = await CardTipAsync(hub, branch, cancellationToken);

			if (await _git.IsAncestorAsync(hub, targetTip, cardTip, cancellationToken))
			{
				return await IntegrateUnlockedAsync(hub, target, branch, targetTip, cardTip, work, cancellationToken);
			}

			GitMergeTree merge = await _git.MergeTreeAsync(hub, cardTip, targetTip, cancellationToken);
			if (merge.IsClean)
			{
				string combined = await _git.CommitTreeAsync(hub, merge.Tree, [cardTip, targetTip], $"Merge {target} into {branch}", cancellationToken);
				if (!await _git.TryMoveBranchAsync(hub, branch, combined, cardTip, cancellationToken))
				{
					return RoundVerdict.Outdated($"{branch} moved while {target} was being merged into it; the next merge round tries again.");
				}

				return RoundVerdict.Outdated($"{target} had moved on, so it was merged into {branch}; the gates check the combination.");
			}

			conflicts = merge.ConflictedFiles;
		}

		// A conflict moves nothing, so what landed is read, and the cards that landed it are looked up, after the lock is released: other integrations do not wait for it.
		string note = string.Create(CultureInfo.InvariantCulture, $"Merging {target} into {branch} conflicts in {conflicts.Count} {(conflicts.Count == 1 ? "file" : "files")}.");

		return RoundVerdict.Failed(note, await ConflictReportAsync(hub, target, cardTip, targetTip, conflicts, describeCard, cancellationToken));
	}

	// The report is what the briefing of whoever resolves the conflict quotes: the files first, then what landed on the target branch since the card's branch last took it in, so the resolver knows what the other side was for. A merge Pyxie wrote names the card that landed it in its trailer, and that card's task is quoted. How to resolve the conflict is not said here: the briefing says it for each kind of round.
	private async Task<string> ConflictReportAsync(string hub, string target, string cardTip, string targetTip, IReadOnlyList<string> files, CardDescriptionLookup describeCard, CancellationToken cancellationToken)
	{
		StringBuilder report = new();
		report.AppendLine("Conflicting files:").AppendLine();
		foreach (string file in files)
		{
			report.Append("- ").AppendLine(file);
		}

		IReadOnlyList<GitCommit> landed = await _git.LogFirstParentAsync(hub, $"{cardTip}..{targetTip}", skip: 0, MaxLandedEntries + 1, cancellationToken);
		if (landed.Count == 0)
		{
			return report.ToString().TrimEnd();
		}

		report.AppendLine().Append(CultureInfo.InvariantCulture, $"Landed on {target} since this branch last took it in, newest first:").AppendLine().AppendLine();
		foreach (GitCommit commit in landed.Take(MaxLandedEntries))
		{
			report.Append(CultureInfo.InvariantCulture, $"- {commit.ShortSha} {commit.Subject}").AppendLine();

			if (commit.CardId is int cardId && await describeCard(cardId, cancellationToken) is string description && !string.IsNullOrWhiteSpace(description))
			{
				report.AppendLine("  Its task:");
				foreach (string line in ReportText.Clip(description.Trim(), MaxQuotedDescriptionLength).ReplaceLineEndings("\n").Split('\n').Select(text => text.TrimEnd()))
				{
					report.AppendLine(line.Length == 0 ? "  >" : $"  > {line}");
				}
			}
		}

		if (landed.Count > MaxLandedEntries)
		{
			report.AppendLine("- (older changes are not listed)");
		}

		return report.ToString().TrimEnd();
	}

	// The subject says what merged; the trailer, which must be the last paragraph for git to read it, names the card, and it is the only place that does: a conflict report reads what landed from it (see ConflictReportAsync).
	private static string MergeMessage(string branch, PipelineWork work)
	{
		return $"Merge {branch}: {work.CardName}\n\n{PipelineContract.CardTrailer(work.CardId)}";
	}

	private static string MergeReport(string mergeCommit, IReadOnlyList<GitCommit> commits)
	{
		StringBuilder report = new();
		report.Append(CultureInfo.InvariantCulture, $"Merge commit {mergeCommit}").AppendLine().AppendLine();
		foreach (GitCommit commit in commits)
		{
			report.Append(CultureInfo.InvariantCulture, $"- {commit.ShortSha} {commit.Subject}").AppendLine();
		}

		return report.ToString().TrimEnd();
	}

	private async Task<string> CardTipAsync(string hub, string branch, CancellationToken cancellationToken)
	{
		return await _git.ResolveCommitAsync(hub, $"refs/heads/{branch}", cancellationToken) ?? throw new GitException($"The card's branch {branch} is not in the repository, so there is nothing to merge. Edit the card so it starts again at work.");
	}

	// The card's branch contains the target branch, so its tree is the merged tree.
	private async Task<RoundVerdict> IntegrateUnlockedAsync(string hub, string target, string branch, string targetTip, string cardTip, PipelineWork work, CancellationToken cancellationToken)
	{
		string tree = await _git.RevParseAsync(hub, $"{cardTip}^{{tree}}", cancellationToken);
		string mergeCommit = await _git.CommitTreeAsync(hub, tree, [targetTip, cardTip], MergeMessage(branch, work), cancellationToken);
		if (!await _git.TryMoveBranchAsync(hub, target, mergeCommit, targetTip, cancellationToken))
		{
			return RoundVerdict.Outdated($"{target} moved on while the merge was being written; the next merge round brings it in.");
		}

		IReadOnlyList<GitCommit> commits = await _git.LogAsync(hub, $"{targetTip}..{cardTip}", 0, 200, cancellationToken);

		return RoundVerdict.Succeeded($"Merged into {target} as {mergeCommit[..Math.Min(12, mergeCommit.Length)]}.", MergeReport(mergeCommit, commits));
	}
}
