using Microsoft.EntityFrameworkCore.Diagnostics;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>A database with the card, agent, and pipeline services on it, plus shortcuts that drive a card through the pipeline the way an orchestrator would, so a test states only the part of the flow it is about.</summary>
/// <remarks>
/// The rig starts with a repository connected (the pipeline claims nothing without one) and with no gates, no agents, and no cards. Which rounds a card gets depends on its setup:
/// <list type="bullet">
/// <item><description>A gate round opens only while a gate is enabled: call <see cref="AddGateAsync"/> first, or the gate stage is recorded as skipped.</description></item>
/// <item><description>A review round opens only for a card with a reviewer: pass <see cref="TestData.DefaultAgent"/> (or an agent) as the reviewer, or the review stage is recorded as skipped.</description></item>
/// <item><description>A merge round always follows, and merging completes the card.</description></item>
/// <item><description>A conflicted merge opens a resolve round only while an agent can merge: call <see cref="AddMergerAsync"/> first, or the conflict goes back to the card's worker as a work round (see <see cref="ConflictAsync"/>).</description></item>
/// </list>
/// Cards added through <see cref="CreateCardAsync"/> or <see cref="TestData.AddCardAsync"/> have the default agent as their worker unless a test says otherwise.
/// </remarks>
public sealed class PipelineRig : IAsyncDisposable
{
	/// <summary>The name <see cref="AddMergerAsync"/> gives the merge agent.</summary>
	public const string MergerName = "Merger";

	/// <summary>The note <see cref="ConflictAsync"/> records unless given another.</summary>
	public const string ConflictNote = "Conflicts in src/Login.cs.";

	/// <summary>Initializes a new instance of the <see cref="PipelineRig"/> class and connects its repository.</summary>
	/// <param name="interceptors">Interceptors attached to every context created on the database.</param>
	public PipelineRig(params IInterceptor[] interceptors)
	{
		Database = new TestDatabase(interceptors);
		Repository = TestData.ConnectRepository(Database);
		Pipeline = new CardPipelineService(Database.ContextFactory, Database.Clock);
		Cards = new CardService(Database.ContextFactory, Database.Clock);
		Agents = new AgentService(Database.ContextFactory, Database.Clock);
	}

	/// <summary>Gets the database.</summary>
	public TestDatabase Database { get; }

	/// <summary>Gets the repository the rig connected, whose target branch is <c>main</c>.</summary>
	public Repository Repository { get; }

	/// <summary>Gets the pipeline service under test.</summary>
	public CardPipelineService Pipeline { get; }

	/// <summary>Gets the card service, as people use it.</summary>
	public CardService Cards { get; }

	/// <summary>Gets the agent service.</summary>
	public AgentService Agents { get; }

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Creates another pipeline service on the same database, standing in for a second pipeline worker.</summary>
	/// <returns>The second service.</returns>
	public CardPipelineService CreateSecondPipeline()
	{
		return new CardPipelineService(Database.ContextFactory, Database.Clock);
	}

	/// <summary>Adds a gate, so cards that reach the gates get a gate round instead of a skipped one.</summary>
	/// <param name="name">The gate name.</param>
	/// <param name="isEnabled">Whether the gate is enabled; a disabled gate does not make gate rounds open.</param>
	/// <returns>The stored gate.</returns>
	public async Task<Gate> AddGateAsync(string name = "Tests", bool isEnabled = true)
	{
		return await TestData.AddGateAsync(Database, name, isEnabled);
	}

	/// <summary>Adds the project's merge agent, so a conflicted merge opens a resolve round instead of sending the card back to its worker. It differs from the default agent in name, harness, command, and arguments, so a test can tell whose snapshot a round holds, and it cannot work or review, so it never takes a card by accident.</summary>
	/// <param name="name">The agent name.</param>
	/// <returns>The stored agent.</returns>
	public async Task<Agent> AddMergerAsync(string name = MergerName)
	{
		return await TestData.AddAgentAsync(Database, name, canWork: false, canReview: false, HarnessKind.Codex, "codex", "exec --full-auto", canMerge: true);
	}

	/// <summary>Removes the repository row (see <see cref="TestData.RemoveRepositoryAsync"/>), so the pipeline claims nothing and records any round it is asked to open as blocked.</summary>
	/// <returns>A task that completes when the repository is gone.</returns>
	public async Task RemoveRepositoryAsync()
	{
		await TestData.RemoveRepositoryAsync(Database);
	}

	/// <summary>Claims the next card and asserts that there was one.</summary>
	/// <returns>The opened round.</returns>
	public async Task<PipelineWork> ClaimAsync()
	{
		PipelineWork? work = await Pipeline.ClaimNextAsync(Token);
		Assert.NotNull(work);

		return work;
	}

	/// <summary>Records a result and asserts that it was accepted.</summary>
	/// <param name="work">The round being finished.</param>
	/// <param name="result">What happened.</param>
	/// <param name="note">The note, if any.</param>
	/// <param name="report">The report, if any.</param>
	/// <returns>The next round, or <see langword="null"/> when the card's run ended.</returns>
	public async Task<PipelineWork?> RecordAsync(PipelineWork work, RoundResult result, string? note = null, string? report = null)
	{
		return ResultAssert.SucceededWithOptionalValue(await Pipeline.RecordResultAsync(work.RoundId, result, new RoundReport(note, report), Token));
	}

	/// <summary>Records a successful result and asserts that another round followed.</summary>
	/// <param name="work">The round being finished.</param>
	/// <param name="report">The report, if any.</param>
	/// <returns>The next round.</returns>
	public async Task<PipelineWork> SucceedAsync(PipelineWork work, string? report = null)
	{
		PipelineWork? next = await RecordAsync(work, RoundResult.Succeeded, report: report);
		Assert.NotNull(next);

		return next;
	}

	/// <summary>Records a conflict on a merge round and asserts that another round followed.</summary>
	/// <param name="merge">The running merge round.</param>
	/// <param name="note">The note that says what conflicted.</param>
	/// <param name="report">The report, if any.</param>
	/// <returns>The next round: a resolve round when the project has a merge agent, otherwise the card's next work round.</returns>
	public async Task<PipelineWork> ConflictAsync(PipelineWork merge, string note = ConflictNote, string? report = null)
	{
		Assert.Equal(RoundKind.Merge, merge.Kind);
		PipelineWork? next = await RecordAsync(merge, RoundResult.Failed, note, report);
		Assert.NotNull(next);

		return next;
	}

	/// <summary>Records successes until the card's run ends, which for a card with nothing in its way is the merge that completes it.</summary>
	/// <param name="work">The running round.</param>
	/// <returns>A task that completes when no round follows.</returns>
	public async Task CompleteAsync(PipelineWork work)
	{
		PipelineWork? current = work;
		while (current is not null)
		{
			current = await RecordAsync(current, RoundResult.Succeeded);
		}
	}

	/// <summary>Claims the next card and records successes until a round of the wanted kind is running for it.</summary>
	/// <param name="kind">The kind of round to stop at.</param>
	/// <returns>The running round of that kind.</returns>
	public async Task<PipelineWork> ClaimAtAsync(RoundKind kind)
	{
		return await AdvanceToAsync(await ClaimAsync(), kind);
	}

	/// <summary>Records successes from a running round until a round of the wanted kind is running for the card.</summary>
	/// <param name="work">The running round.</param>
	/// <param name="kind">The kind of round to stop at; it must not come before <paramref name="work"/> in the pipeline (work or resolve, gate, review, merge). A resolve round opens only after a conflicted merge, so it can only be the round already running: use <see cref="ConflictAsync"/> to reach one.</param>
	/// <returns>The running round of that kind.</returns>
	/// <exception cref="InvalidOperationException">The card went past the wanted kind because that stage does not apply to it: a gate round needs an enabled gate and a review round needs a reviewer. Or a resolve round is wanted from another kind of round, which successes never lead to.</exception>
	public async Task<PipelineWork> AdvanceToAsync(PipelineWork work, RoundKind kind)
	{
		if (kind == RoundKind.Resolve && work.Kind != RoundKind.Resolve)
		{
			throw new InvalidOperationException("A resolve round only opens after a conflicted merge, never after a success: record the conflict with ConflictAsync, with a merge agent added (AddMergerAsync).");
		}

		PipelineWork current = work;
		while (current.Kind != kind)
		{
			if (StageOf(current.Kind) > StageOf(kind))
			{
				throw new InvalidOperationException($"No {kind} round opened: the card is at {current.Kind} already. A gate round needs an enabled gate (AddGateAsync) and a review round needs a reviewer.");
			}

			current = await SucceedAsync(current);
		}

		return current;
	}

	// A resolve round stands in the pipeline where the work round does: its success leads to the gates.
	private static int StageOf(RoundKind kind)
	{
		return kind switch
		{
			RoundKind.Work or RoundKind.Resolve => 0,
			RoundKind.Gate => 1,
			RoundKind.Review => 2,
			RoundKind.Merge => 3,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown round kind."),
		};
	}

	/// <summary>Adds a card to the Queue through the card service, as a person would.</summary>
	/// <param name="name">The card name.</param>
	/// <param name="workerAgentId">The worker agent: the default agent unless given, <see langword="null"/> for none (which the service refuses).</param>
	/// <param name="reviewerAgentId">The reviewer agent: none unless given, <see cref="TestData.DefaultAgent"/> for the default agent.</param>
	/// <param name="description">The card description; a non-blank placeholder unless given.</param>
	/// <param name="attemptLimit">The card's attempt limit; the default limit unless given.</param>
	/// <returns>The created card.</returns>
	public async Task<WorkCard> CreateCardAsync(string name, int? workerAgentId = TestData.DefaultAgent, int? reviewerAgentId = null, string description = TestCardInput.DefaultDescription, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		CardInput input = await TestCardInput.CreateAsync(Database, name, workerAgentId: workerAgentId, reviewerAgentId: reviewerAgentId, description: description, attemptLimit: attemptLimit);

		return ResultAssert.Succeeded(await Cards.CreateAsync(input, Token));
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Database.DisposeAsync();
	}
}
