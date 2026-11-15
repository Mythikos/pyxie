using System.Globalization;
using System.Text;

using Microsoft.Extensions.Options;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Gates;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Integration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Orchestration;

/// <summary>Runs one round for the open project and says how it went. Work, resolve, gate, and review rounds run in the card's sandbox (see <c>docs/sandboxing.md</c>): the agent for work, resolve, and review rounds, with the changes of a worker or merge agent committed and taken back into the hub afterwards, and the gates for gate rounds. Merge rounds run in the hub, without a sandbox. Failures that need a person are blocked; failures another attempt may get past are errors (see <c>docs/pipeline.md</c>). A container engine that stops answering is not the card's fault: it is thrown as <see cref="SandboxUnavailableException"/>, so the round is given back instead.</summary>
public sealed class RoundExecutor
{
	// A resolution's report is the merge agent's own report and then how the merge resolved the conflicts, each kept within these limits so both fit the limit of a round's report.
	private const int MaxAgentReportLength = 40_000;
	private const int AgentReportHeadLength = 10_000;
	private const int MaxDiffLength = 40_000;

	private readonly OpenProject _openProject;
	private readonly IRepositoryService _repositories;
	private readonly IGateService _gates;
	private readonly ISandboxSettingsService _sandboxSettings;
	private readonly ICardService _cardService;
	private readonly CardSandboxes _cards;
	private readonly GitClient _git;
	private readonly HarnessRunner _harness;
	private readonly GateRunner _gateRunner;
	private readonly CardIntegrator _integrator;
	private readonly PipelineOptions _options;

	/// <summary>Initializes a new instance of the <see cref="RoundExecutor"/> class.</summary>
	/// <param name="openProject">The open project, whose folder holds the round logs.</param>
	/// <param name="repositories">The repository service.</param>
	/// <param name="gates">The gate service.</param>
	/// <param name="sandboxSettings">The project's sandbox settings, which name its image.</param>
	/// <param name="cardService">The card service, which tells a merge's report what the cards that landed on the target branch were for.</param>
	/// <param name="cards">Runs the card's rounds in sandboxes.</param>
	/// <param name="git">The git client, for reading the hub after a resolution is taken back.</param>
	/// <param name="harness">The agent runner.</param>
	/// <param name="gateRunner">The gate runner.</param>
	/// <param name="integrator">The merge step.</param>
	/// <param name="options">The pipeline options.</param>
	public RoundExecutor(OpenProject openProject, IRepositoryService repositories, IGateService gates, ISandboxSettingsService sandboxSettings, ICardService cardService, CardSandboxes cards, GitClient git, HarnessRunner harness, GateRunner gateRunner, CardIntegrator integrator, IOptions<PipelineOptions> options)
	{
		_openProject = openProject;
		_repositories = repositories;
		_gates = gates;
		_sandboxSettings = sandboxSettings;
		_cardService = cardService;
		_cards = cards;
		_git = git;
		_harness = harness;
		_gateRunner = gateRunner;
		_integrator = integrator;
		_options = options.Value;
	}

	private TimeSpan RoundTimeout => TimeSpan.FromMinutes(_options.RoundTimeoutMinutes);

	/// <summary>Runs a round.</summary>
	/// <param name="work">The round the pipeline opened.</param>
	/// <param name="cancellationToken">A token that stops the round when cancelled.</param>
	/// <returns>The verdict to record, and the repository it ran against when the project has one.</returns>
	/// <exception cref="OperationCanceledException">The token was cancelled; anything the round started has been stopped.</exception>
	/// <exception cref="SandboxUnavailableException">The container engine does not answer.</exception>
	public async Task<(RoundVerdict Verdict, Repository? Repository)> RunAsync(PipelineWork work, CancellationToken cancellationToken)
	{
		if (_openProject.Current is not Project project)
		{
			return (RoundVerdict.Blocked("No project is open."), null);
		}

		Repository? repository = await _repositories.GetAsync(cancellationToken);
		if (repository is null)
		{
			return (RoundVerdict.Blocked("No repository is connected."), null);
		}

		try
		{
			return (await RunInRepositoryAsync(project.Folder, repository, work, cancellationToken), repository);
		}
		catch (GitException exception)
		{
			return (RoundVerdict.Blocked($"git failed: {FirstLine(exception.Message)}", exception.Message), repository);
		}
		catch (SandboxException exception)
		{
			return (RoundVerdict.Blocked(FirstLine(exception.Message), exception.Message), repository);
		}
	}

	private static string FirstLine(string text)
	{
		int end = text.IndexOf('\n', StringComparison.Ordinal);

		return end < 0 ? text : text[..end];
	}

	private static string CommitMessage(PipelineWork work)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{work.CardName}\n\nCard {work.CardId}, attempt {work.Attempt}.");
	}

	// A fence of backticks longer than any run of them in the text, so a diff that has some of its own cannot end the block early.
	private static string FenceFor(string text)
	{
		int longest = 0;
		int run = 0;
		foreach (char character in text)
		{
			run = character == '`' ? run + 1 : 0;
			longest = Math.Max(longest, run);
		}

		return new string('`', Math.Max(3, longest + 1));
	}

	private async Task<string?> DescribeCardAsync(int cardId, CancellationToken cancellationToken)
	{
		ServiceResult<WorkCard> card = await _cardService.GetAsync(cardId, cancellationToken);

		return card.Succeeded ? card.Value?.Description : null;
	}

	private async Task<RoundVerdict> RunInRepositoryAsync(string projectFolder, Repository repository, PipelineWork work, CancellationToken cancellationToken)
	{
		if (work.Kind == RoundKind.Merge)
		{
			return await _integrator.IntegrateAsync(repository, work, DescribeCardAsync, cancellationToken);
		}

		if (work.Kind is not (RoundKind.Work or RoundKind.Resolve or RoundKind.Review or RoundKind.Gate))
		{
			return RoundVerdict.Blocked($"Pyxie does not know how to run a {work.Kind} round.");
		}

		if (work.Kind == RoundKind.Resolve && await FinishResolveAlreadyMergedAsync(repository, work, cancellationToken) is RoundVerdict finished)
		{
			return finished;
		}

		SandboxSettings settings = await _sandboxSettings.GetAsync(cancellationToken);
		CardRoundContext context = new(projectFolder, SandboxNames.ProjectKey(projectFolder), repository, settings.Dockerfile);
		string logPath = WorkspaceLayout.RoundLogPath(projectFolder, work.CardId, work.Sequence);

		await using CardSandbox card = await _cards.OpenAsync(context, work, cancellationToken);

		return work.Kind switch
		{
			RoundKind.Work => await RunWorkAsync(context, card, work, logPath, cancellationToken),
			RoundKind.Resolve => await RunResolveAsync(context, card, work, logPath, cancellationToken),
			RoundKind.Review => await _harness.RunAsync(work, card.Sandbox, card.RepositoryPath, logPath, RoundTimeout, cancellationToken),
			RoundKind.Gate => await RunGatesAsync(card, logPath, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(work), work.Kind, "Only work, resolve, review, and gate rounds run in a sandbox."),
		};
	}

	// The worker's changes are committed on the card's branch and taken back into the hub; a commit git refuses (a hook, say) is worth another attempt with the reason in the next briefing. Whatever the worker left running is ended first, so nothing changes the sandbox while Pyxie reads it.
	private async Task<RoundVerdict> RunWorkAsync(CardRoundContext context, CardSandbox card, PipelineWork work, string logPath, CancellationToken cancellationToken)
	{
		RoundVerdict verdict = await _harness.RunAsync(work, card.Sandbox, card.RepositoryPath, logPath, RoundTimeout, cancellationToken);
		if (verdict.Result != RoundResult.Succeeded || card.Sandbox.IsSpoiled)
		{
			return verdict;
		}

		await card.Sandbox.EndProcessesAsync(cancellationToken);

		if (await _cards.CommitAsync(card, CommitMessage(work), cancellationToken) is string commitProblem)
		{
			return RoundVerdict.Errored($"Committing the worker's changes failed: {commitProblem}");
		}

		if (await _cards.TakeBackAsync(context, card, cancellationToken) is string takeBackProblem)
		{
			return RoundVerdict.Errored(takeBackProblem);
		}

		return verdict;
	}

	// The merge agent resolves the merge Pyxie started in its sandbox. Its resolution is committed as the merge commit (Pyxie finishes the merge when the agent left it open) and taken back only when it is exactly one merge commit of the card's branch and the target branch without conflict markers left in the files that conflicted; anything else is an error the next attempt starts over from. Once the branch has moved nothing may fail the round: how the merge resolved the conflicts is read for the report, which is best effort, so the result is recorded and the reviewer, who sees the same diff in the briefing, can check it.
	private async Task<RoundVerdict> RunResolveAsync(CardRoundContext context, CardSandbox card, PipelineWork work, string logPath, CancellationToken cancellationToken)
	{
		RoundVerdict verdict = await _harness.RunAsync(work, card.Sandbox, card.RepositoryPath, logPath, RoundTimeout, cancellationToken);
		if (verdict.Result != RoundResult.Succeeded || card.Sandbox.IsSpoiled)
		{
			return verdict;
		}

		await card.Sandbox.EndProcessesAsync(cancellationToken);

		string target = context.Repository.TargetBranch;
		if (await _cards.CommitAsync(card, $"Merge {target} into {card.Branch}", cancellationToken) is string commitProblem)
		{
			return RoundVerdict.Errored($"Committing the merge failed: {commitProblem}");
		}

		if (await _cards.TakeBackResolutionAsync(context, card, cancellationToken) is string takeBackProblem)
		{
			return RoundVerdict.Errored(takeBackProblem);
		}

		string hub = context.Repository.Path;
		string merge = await _git.RevParseAsync(hub, $"refs/heads/{card.Branch}", CancellationToken.None);
		string note = string.IsNullOrWhiteSpace(verdict.Note) ? $"Resolved the conflicts of merging {target}." : verdict.Note;

		return RoundVerdict.Succeeded(note, await ResolutionReportAsync(hub, merge, verdict.Report));
	}

	// A resolve round that finds the target branch already merged into the card's branch has nothing left to resolve: an earlier resolve round moved the branch to its merge commit and was cut off (the pipeline stopped, Pyxie shut down) before its result was recorded. Starting a merge that has nothing to merge would leave no merge to complete, and the card would fail every attempt and every retry. The round succeeds without an agent, and the gates and the review check the merge as they would have.
	private async Task<RoundVerdict?> FinishResolveAlreadyMergedAsync(Repository repository, PipelineWork work, CancellationToken cancellationToken)
	{
		string hub = repository.Path;
		string target = repository.TargetBranch;
		string branch = PipelineContract.BranchName(work.CardId);

		string? cardTip = await _git.ResolveCommitAsync(hub, $"refs/heads/{branch}", cancellationToken);
		string targetTip = await _git.RevParseAsync(hub, $"refs/heads/{target}", cancellationToken);
		if (cardTip is null || !await _git.IsAncestorAsync(hub, targetTip, cardTip, cancellationToken))
		{
			return null;
		}

		return RoundVerdict.Succeeded($"{target} was already merged into {branch}.", await ResolutionReportAsync(hub, cardTip, agentReport: null));
	}

	// The agent's own report, then the merge commit and how it resolved the conflicts compared with what git did by itself, which is what a reviewer needs to judge a resolution. The last section is the resolution, because a briefing that has to cut a long report keeps the end of it.
	private async Task<string> ResolutionReportAsync(string hub, string merge, string? agentReport)
	{
		StringBuilder report = new();
		if (!string.IsNullOrWhiteSpace(agentReport))
		{
			report.AppendLine(ReportText.Excerpt(agentReport.Trim(), MaxAgentReportLength, AgentReportHeadLength)).AppendLine();
		}

		report.AppendLine("## Resolution").AppendLine();

		string diff;
		try
		{
			diff = await _git.RemergeDiffAsync(hub, merge, MaxDiffLength, CancellationToken.None);
		}
		catch (GitException exception)
		{
			return report.Append(CultureInfo.InvariantCulture, $"Merge commit {merge}. git could not show how it resolved the conflicts: {FirstLine(exception.Message)}").ToString();
		}

		if (diff.Length == 0)
		{
			return report.Append(CultureInfo.InvariantCulture, $"Merge commit {merge}. `git show --remerge-diff` shows nothing: the merge had no conflicts to resolve.").ToString();
		}

		string fence = FenceFor(diff);
		report.Append(CultureInfo.InvariantCulture, $"Merge commit {merge}, as `git show --remerge-diff` shows it:").AppendLine().AppendLine();
		report.Append(fence).AppendLine("diff");
		report.AppendLine(diff);
		report.Append(fence);

		return report.ToString();
	}

	private async Task<RoundVerdict> RunGatesAsync(CardSandbox card, string logPath, CancellationToken cancellationToken)
	{
		List<Gate> enabled = [.. (await _gates.ListAsync(cancellationToken)).Where(gate => gate.IsEnabled)];

		return await _gateRunner.RunAsync(enabled, card.Sandbox, card.RepositoryPath, logPath, cancellationToken);
	}
}
