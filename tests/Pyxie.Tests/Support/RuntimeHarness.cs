using System.Globalization;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Sqlite.DependencyInjection;
using Pyxie.Runtime.DependencyInjection;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>The whole runtime for a test, wired the way <c>Program.cs</c> wires it: the SQLite provider, the data services, <c>AddPyxieRuntime</c>, and the real <see cref="PipelineOrchestrator"/>, with <c>Workspace:Root</c> in a temporary folder of its own (holding the machine database and the project folders) and the system clock. Tests import a <see cref="TempGitRepository"/>, which creates and opens a project, add fake agents, gates, and cards, start the pipeline, and wait for what they expect on the board. Disposing the harness stops the pipeline and deletes everything it created, the project folder and its hub included.</summary>
/// <remarks>
/// <para>
/// Typical use, with the source declared first so it is deleted last:
/// </para>
/// <code>
/// await using TempGitRepository source = await TempGitRepository.CreateAsync();
/// await using RuntimeHarness harness = new();
/// await harness.ConnectRepositoryAsync(source);
/// Agent worker = await harness.AddWorkerAsync("Worker", "--write", "feature.txt=hello");
/// WorkCard card = await harness.AddCardAsync("Add the feature", worker);
/// await harness.StartPipelineAsync();
/// await harness.WaitForLaneAsync(card.Id, CardLane.Complete);
/// Assert.Equal("hello", await harness.Clone.ReadFileAtAsync("main", "feature.txt"));
/// </code>
/// <para>
/// Connecting imports the source the way a person does: Pyxie creates a project folder, clones the source into its hub, creates the project's database, and opens the project. Until then no project is open, so agents, gates, and cards can only be added after connecting. The pipeline never changes the source, so finished cards land in <see cref="Clone"/>, which is where a test reads <c>main</c>, its files, and its merge commits, and where it sets up worktrees and branches while the pipeline runs. To move <c>main</c> on the way a person does, a test clones <see cref="Clone"/> with <c>ClonedRepository</c>, commits there, and pushes.
/// </para>
/// <para>
/// The pipeline starts stopped, as it does in Pyxie: cards wait in the Queue until <see cref="StartPipelineAsync"/>. A card gets a gate round only once the test has added an enabled gate (<see cref="AddGateAsync(string, string[])"/>) and a review round only when the test gave it a reviewer; without them those stages are recorded as skipped (see <c>docs/pipeline.md</c>).
/// </para>
/// <para>
/// The databases are the files Pyxie itself uses, not a <see cref="TestDatabase"/>: <c>TestDatabase</c> gives every context one shared connection, which throws "does not support nested transactions" as soon as two contexts overlap, and the orchestrator, its workers, and the test's own calls do overlap. Here every context has its own connection and meets SQLite's real locking.
/// </para>
/// <para>
/// Time is the system clock (<see cref="TimeProvider.System"/>), which the process runner needs for real timeouts. Pipeline and gate timeouts are whole minutes with a one-minute minimum, so a test of a timeout cannot wait one out through the pipeline; drive <c>HarnessRunner</c>, <c>GateRunner</c>, or <c>ProcessRunner</c> directly with a short <see cref="TimeSpan"/> instead. Logging goes nowhere, so a failed wait reports the board and every round's note instead (see <see cref="WaitUntilAsync"/>).
/// </para>
/// <para>
/// Rounds run in the local stand-in sandboxes (<see cref="LocalSandboxes"/>), as host processes in folders under the harness's own folder, so no container engine is needed; <see cref="WorkerRepository"/> is where a card's worker works. The harness keeps its folder names short, because git on Windows refuses some paths longer than about 215 characters.
/// </para>
/// </remarks>
public sealed class RuntimeHarness : IAsyncDisposable
{
	private static readonly TimeSpan s_defaultWait = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan s_stopTimeout = TimeSpan.FromSeconds(30);

	// The change signal wakes a wait at once; this only bounds how long a change that does not announce itself (a gate or agent edit, say) goes unnoticed.
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(250);

	private readonly string _folder;
	private readonly ServiceProvider _provider;
	private readonly PipelineOrchestrator _orchestrator;
	private TempGitRepository? _clone;
	private bool _orchestratorStarted;
	private bool _orchestratorStopped;
	private bool _disposed;

	/// <summary>Initializes a new instance of the <see cref="RuntimeHarness"/> class: builds the services and the machine database, with no project. Nothing runs until <see cref="StartPipelineAsync"/>.</summary>
	/// <param name="workers">How many cards the pipeline runs at once (<c>Pipeline:Workers</c>, 1 to 32). One keeps rounds in a predictable order and is required for fake agents that use a script.</param>
	/// <param name="roundTimeoutMinutes">How long an agent may run in one round (<c>Pipeline:RoundTimeoutMinutes</c>, 1 to 1440). It bounds a fake agent that hangs, so keep it small.</param>
	public RuntimeHarness(int workers = 1, int roundTimeoutMinutes = 5)
	{
		_folder = TempFolder.Create("pyxie-rt");
		WorkspaceRoot = Path.Combine(_folder, "ws");
		ScriptsFolder = Path.Combine(_folder, "scripts");
		Directory.CreateDirectory(ScriptsFolder);

		Dictionary<string, string?> settings = new()
		{
			["Workspace:Root"] = WorkspaceRoot,
			["Pipeline:Workers"] = workers.ToString(CultureInfo.InvariantCulture),
			["Pipeline:PollIntervalSeconds"] = "1",
			["Pipeline:RoundTimeoutMinutes"] = roundTimeoutMinutes.ToString(CultureInfo.InvariantCulture),
		};

		IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		ServiceCollection services = new();
		services.AddLogging();
		services.AddPyxieSqlite(provider => provider.GetRequiredService<WorkspaceLayout>().MachineDatabasePath);
		services.AddPyxieData();
		services.AddPyxieRuntime(configuration);
		services.AddLocalSandboxes(Path.Combine(_folder, "sandboxes"));
		_provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		Layout = _provider.GetRequiredService<WorkspaceLayout>();
		Directory.CreateDirectory(Layout.Root);
		using (MachineDbContext machine = _provider.GetRequiredService<IDbContextFactory<MachineDbContext>>().CreateDbContext())
		{
			machine.Database.Migrate();
		}

		_orchestrator = _provider.GetServices<IHostedService>().OfType<PipelineOrchestrator>().Single();
		Controller = _provider.GetRequiredService<PipelineController>();
		Activity = _provider.GetRequiredService<PipelineActivity>();
		Git = _provider.GetRequiredService<GitClient>();
		OpenProject = _provider.GetRequiredService<OpenProject>();
		ContextFactory = _provider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();
	}

	/// <summary>Gets the stand-in sandboxes the rounds run in.</summary>
	public LocalSandboxes Sandboxes => (LocalSandboxes)_provider.GetRequiredService<ISandboxes>();

	/// <summary>Gets the folder that holds the machine database and the project folders (<c>Workspace:Root</c>). It belongs to this harness and is deleted with it.</summary>
	public string WorkspaceRoot { get; }

	/// <summary>Gets a folder for fake-agent scripts, outside every repository and card workspace, so neither a worker's commit nor git's cleaning touches a script's run counter.</summary>
	public string ScriptsFolder { get; }

	/// <summary>Gets the runtime's services, for the few things the helpers do not cover. Scoped services (the card, agent, gate, and repository services) must be resolved from <see cref="CreateScope"/>.</summary>
	public IServiceProvider Services => _provider;

	/// <summary>Gets the controller that starts and stops the pipeline.</summary>
	public PipelineController Controller { get; }

	/// <summary>Gets the signal that cards changed. Call <see cref="NotifyCardsChanged"/> after changing cards through a service, as the pages do.</summary>
	public PipelineActivity Activity { get; }

	/// <summary>Gets the runtime's git client.</summary>
	public GitClient Git { get; }

	/// <summary>Gets the workspace layout, which names the project folders.</summary>
	public WorkspaceLayout Layout { get; }

	/// <summary>Gets the open project, which <see cref="ConnectRepositoryAsync"/> opens.</summary>
	public OpenProject OpenProject { get; }

	/// <summary>Gets the open project's folder, which holds its database, its hub, the host side of the card sandboxes, and the round logs.</summary>
	/// <exception cref="InvalidOperationException">No repository is connected yet, so no project is open.</exception>
	public string ProjectFolder => OpenProject.Current?.Folder ?? throw new InvalidOperationException($"No project is open: call {nameof(ConnectRepositoryAsync)} first.");

	/// <summary>Gets the factory the services create their short-lived contexts on the open project's database from, for tests that read the database directly.</summary>
	public IDbContextFactory<ProjectDbContext> ContextFactory { get; }

	/// <summary>Gets the connected repository as the repository service stores it, or <see langword="null"/> before <see cref="ConnectRepositoryAsync"/>. Its <c>Path</c> is the project's hub.</summary>
	public Repository? Repository { get; private set; }

	/// <summary>Gets Pyxie's clone of the connected repository, the hub, which is the only repository the pipeline writes to: merges land here and card branches come back into it from the sandboxes. It is a view over a bare repository (<see cref="TempGitRepository.IsBare"/>): it reads and branches like the source <see cref="TempGitRepository"/>, but has no working tree to commit in. The harness owns the folder and deletes it with the workspace root.</summary>
	/// <exception cref="InvalidOperationException">No repository is connected yet.</exception>
	public TempGitRepository Clone => _clone ?? throw new InvalidOperationException($"No repository is connected: call {nameof(ConnectRepositoryAsync)} first.");

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Gets the folder that stands for a card's worker sandbox's repository, which is kept between the card's work rounds.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The full path.</returns>
	public string WorkerRepository(int cardId)
	{
		return Path.Combine(Sandboxes.WorkFolder(SandboxNames.Container(SandboxNames.ProjectKey(ProjectFolder), cardId, SandboxRole.Worker)), "repo");
	}

	/// <summary>Creates a scope for resolving the scoped data services, the way the orchestrator and the pages do. The caller disposes it.</summary>
	/// <returns>The scope.</returns>
	public AsyncServiceScope CreateScope()
	{
		return _provider.CreateAsyncScope();
	}

	/// <summary>Imports a temporary repository the way a person does, through <see cref="ProjectSetupService.ImportFolderAsync"/>: Pyxie creates and opens a project named after the source, clones the source into the project's hub, and its checked-out branch (<c>main</c>) becomes the target branch. The source is never changed from here on; see <see cref="Clone"/> for the repository the pipeline works in.</summary>
	/// <param name="source">The repository to import. It stays owned by the test, which declares it before the harness so it is deleted last.</param>
	/// <returns>The connected repository, read back from the repository service. The test fails when the import is refused.</returns>
	public async Task<Repository> ConnectRepositoryAsync(TempGitRepository source)
	{
		await using AsyncServiceScope scope = CreateScope();
		ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ProjectSetupService>().ImportFolderAsync(source.Folder, Token));
		Repository connected = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().GetAsync(Token) ?? throw new InvalidOperationException("The import succeeded but no repository is stored.");
		Repository = connected;
		_clone = await TempGitRepository.AttachAsync(connected.Path);
		NotifyCardsChanged();

		return connected;
	}

	/// <summary>Adds an agent through the agent service.</summary>
	/// <param name="input">The agent, usually from <see cref="FakeHarnessAgent.Input"/> or <see cref="FakeHarnessAgent.ScriptInput"/>.</param>
	/// <returns>The stored agent. The test fails when the service refuses it.</returns>
	public async Task<Agent> AddAgentAsync(AgentInput input)
	{
		await using AsyncServiceScope scope = CreateScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<IAgentService>().CreateAsync(input, Token));
	}

	/// <summary>Adds a fake agent that can work (and cannot review), with the <see cref="HarnessKind.Other"/> harness.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument (see <see cref="FakeHarnessAgent"/>). With none, the worker completes without changing anything.</param>
	/// <returns>The stored agent.</returns>
	public async Task<Agent> AddWorkerAsync(string name, params string[] fakeArguments)
	{
		return await AddAgentAsync(FakeHarnessAgent.Input(name, HarnessKind.Other, canWork: true, canReview: false, fakeArguments));
	}

	/// <summary>Adds a fake agent that can review (and cannot work), with the <see cref="HarnessKind.Other"/> harness.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument. A review round accepts the outcomes <c>approved</c>, <c>rejected</c>, <c>errored</c>, and <c>blocked</c>, so a reviewer needs <c>--outcome approved</c> at least.</param>
	/// <returns>The stored agent.</returns>
	public async Task<Agent> AddReviewerAsync(string name, params string[] fakeArguments)
	{
		return await AddAgentAsync(FakeHarnessAgent.Input(name, HarnessKind.Other, canWork: false, canReview: true, fakeArguments));
	}

	/// <summary>Adds a fake agent that is the project's merge agent (and can neither work nor review), with the <see cref="HarnessKind.Other"/> harness. A merge conflict then goes to it in a resolve round instead of back to the card's worker. A project has at most one.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument. A resolve round accepts the outcomes <c>resolved</c>, <c>errored</c>, and <c>blocked</c>, so a merge agent needs <c>--outcome resolved</c> at least, and <c>--write</c> of the resolved file, which Pyxie then commits as the merge.</param>
	/// <returns>The stored agent.</returns>
	public async Task<Agent> AddMergerAsync(string name, params string[] fakeArguments)
	{
		return await AddAgentAsync(new AgentInput(name, HarnessKind.Other, FakeHarnessAgent.Command, FakeHarnessAgent.Arguments(HarnessKind.Other, fakeArguments), CanWork: false, CanReview: false, CanMerge: true));
	}

	/// <summary>Adds a gate through the gate service, after the gates that exist. An enabled gate makes cards get gate rounds instead of a skipped one.</summary>
	/// <param name="input">The gate.</param>
	/// <returns>The stored gate. The test fails when the service refuses it.</returns>
	public async Task<Gate> AddGateAsync(GateInput input)
	{
		await using AsyncServiceScope scope = CreateScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<IGateService>().CreateAsync(input, Token));
	}

	/// <summary>Adds an enabled gate that runs the fake harness: it passes when it exits with code 0 and fails on any other code (<c>--exit 1</c>), with its output in the report.</summary>
	/// <param name="name">The unique gate name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument.</param>
	/// <returns>The stored gate, with a five-minute timeout.</returns>
	public async Task<Gate> AddGateAsync(string name, params string[] fakeArguments)
	{
		return await AddGateAsync(new GateInput(name, FakeHarnessAgent.Command, FakeHarnessAgent.Arguments(HarnessKind.Other, fakeArguments), TimeoutMinutes: 5, IsEnabled: true));
	}

	/// <summary>Adds a card to the bottom of the Queue through the card service, as a person would, and wakes idle pipeline workers.</summary>
	/// <param name="name">The card name.</param>
	/// <param name="worker">The worker agent.</param>
	/// <param name="reviewer">The reviewer agent; none unless given, which skips the review stage.</param>
	/// <param name="description">The card description; a non-blank placeholder unless given.</param>
	/// <param name="attemptLimit">How many attempts each run of the card gets; the default limit unless given.</param>
	/// <returns>The created card. The test fails when the service refuses it.</returns>
	public async Task<WorkCard> AddCardAsync(string name, Agent worker, Agent? reviewer = null, string description = TestCardInput.DefaultDescription, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		CardInput input = TestCardInput.Create(name, PyxieSeedData.FeatureCategoryId, worker.Id, reviewer?.Id, description, attemptLimit);
		await using AsyncServiceScope scope = CreateScope();
		WorkCard card = ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().CreateAsync(input, Token));
		NotifyCardsChanged();

		return card;
	}

	/// <summary>Writes a script for a fake agent whose runs differ (see <see cref="FakeHarnessAgent.WriteScriptAsync"/>), into <see cref="ScriptsFolder"/>.</summary>
	/// <param name="runs">The behavior arguments of each run, each one argument.</param>
	/// <returns>The script's full path, for <see cref="FakeHarnessAgent.ScriptInput"/>.</returns>
	public async Task<string> WriteScriptAsync(params string[][] runs)
	{
		return await FakeHarnessAgent.WriteScriptAsync(ScriptsFolder, runs);
	}

	/// <summary>Tells idle pipeline workers that cards changed, so they look for work at once instead of at the next poll. The helpers that change cards do this themselves; call it after changing cards through a service.</summary>
	public void NotifyCardsChanged()
	{
		Activity.NotifyCardsChanged();
	}

	/// <summary>Starts the orchestrator, as Pyxie does at launch, but leaves the pipeline stopped: it recovers rounds a previous run left open and then waits. Does nothing when it already started. Use it, then <see cref="PipelineController.Start"/>, for a test about the stopped state. Otherwise call <see cref="StartPipelineAsync"/>.</summary>
	/// <returns>A task that completes when the orchestrator has started.</returns>
	/// <exception cref="InvalidOperationException">The orchestrator was shut down with <see cref="StopOrchestratorAsync"/>, which cannot be undone.</exception>
	public async Task StartOrchestratorAsync()
	{
		if (_orchestratorStopped)
		{
			throw new InvalidOperationException("The orchestrator was shut down and cannot be started again; stop and start the pipeline through its controller instead.");
		}

		if (_orchestratorStarted)
		{
			return;
		}

		_orchestratorStarted = true;
		await _orchestrator.StartAsync(Token);
	}

	/// <summary>Starts the orchestrator if needed and starts the pipeline, which then claims cards from the top of the Queue.</summary>
	/// <returns>A task that completes when the pipeline is started. The test fails when the controller refuses to start, for example because no repository was connected, so no project is open.</returns>
	public async Task StartPipelineAsync()
	{
		await StartOrchestratorAsync();
		ResultAssert.Succeeded(Controller.Start());
	}

	/// <summary>Shuts the orchestrator down, as Pyxie does when it exits: running rounds are killed and recorded as interrupted by the shutdown, and finished merges clean up their workspaces. The orchestrator cannot be started again (<see cref="StartOrchestratorAsync"/> then throws), so this ends the pipeline for the rest of the test. To stop and resume the pipeline, call <see cref="PipelineController.Stop"/> and <see cref="PipelineController.Start"/>.</summary>
	/// <returns>A task that completes when the orchestrator has stopped. The test fails when it does not stop within thirty seconds.</returns>
	public async Task StopOrchestratorAsync()
	{
		Controller.Stop();
		if (!_orchestratorStarted || _orchestratorStopped)
		{
			return;
		}

		_orchestratorStopped = true;
		using CancellationTokenSource timeout = new(s_stopTimeout);
		await _orchestrator.StopAsync(timeout.Token);
		Assert.True(_orchestrator.ExecuteTask is not { IsCompleted: false }, $"The pipeline did not stop within {s_stopTimeout.TotalSeconds:0} seconds.");
	}

	/// <summary>Gets the board as people see it: every card that is not closed, with its history summary.</summary>
	/// <returns>The cards, ordered by lane then position.</returns>
	public async Task<IReadOnlyList<BoardCard>> GetBoardAsync()
	{
		await using AsyncServiceScope scope = CreateScope();

		return await scope.ServiceProvider.GetRequiredService<ICardService>().GetBoardAsync(Token);
	}

	/// <summary>Gets a card's rounds, oldest first, with their agent snapshots, notes, and reports.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The rounds. The test fails when the card does not exist.</returns>
	public async Task<IReadOnlyList<CardRound>> GetHistoryAsync(int cardId)
	{
		await using AsyncServiceScope scope = CreateScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().GetHistoryAsync(cardId, Token));
	}

	/// <summary>Waits until a condition holds, checking at once, whenever cards change, and at least every quarter second. Use it for what the board cannot show, such as a branch being gone or a file existing; use <see cref="WaitForCardAsync"/> for a card.</summary>
	/// <param name="condition">The condition to check. It must not throw while it is false.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ..." in the failure message.</param>
	/// <param name="timeout">How long to wait; a minute unless given.</param>
	/// <returns>A task that completes when the condition holds. The test fails with <paramref name="description"/> and the state of the board (each card's lane and state, and each round's kind, outcome, and note) when it does not.</returns>
	public async Task WaitUntilAsync(Func<Task<bool>> condition, string description, TimeSpan? timeout = null)
	{
		TimeSpan limit = timeout ?? s_defaultWait;
		using CancellationTokenSource expired = CancellationTokenSource.CreateLinkedTokenSource(Token);
		expired.CancelAfter(limit);

		try
		{
			while (true)
			{
				// Taken before the check, so a change that lands while checking still wakes the wait below.
				Task changed = Activity.NextChangeAsync();
				if (await condition())
				{
					return;
				}

				await Task.WhenAny(changed, Task.Delay(s_pollInterval, expired.Token));
				expired.Token.ThrowIfCancellationRequested();
			}
		}
		catch (OperationCanceledException) when (expired.IsCancellationRequested && !Token.IsCancellationRequested)
		{
			Assert.Fail($"Timed out waiting for {description} after {limit.TotalSeconds:0.##} seconds.{Environment.NewLine}{await DescribeBoardAsync()}");
		}
	}

	/// <summary>Waits until a card on the board satisfies a condition.</summary>
	/// <param name="cardId">The card identifier. A card that is not on the board (deleted or closed) never satisfies it.</param>
	/// <param name="condition">The condition over the card and its history summary.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ..." in the failure message.</param>
	/// <param name="timeout">How long to wait; a minute unless given.</param>
	/// <returns>The card as it was when the condition held.</returns>
	public async Task<BoardCard> WaitForCardAsync(int cardId, Func<BoardCard, bool> condition, string description, TimeSpan? timeout = null)
	{
		BoardCard? found = null;
		await WaitUntilAsync(
			async () =>
			{
				found = (await GetBoardAsync()).FirstOrDefault(card => card.Card.Id == cardId && condition(card));

				return found is not null;
			},
			description,
			timeout);

		return found ?? throw new InvalidOperationException($"Card {cardId} was not found although the wait for {description} completed.");
	}

	/// <summary>Waits until a card is in a lane. A card that finished its run waits in <see cref="CardLane.Complete"/>.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="lane">The lane to wait for.</param>
	/// <param name="timeout">How long to wait; a minute unless given.</param>
	/// <returns>The card as it was when it reached the lane.</returns>
	public async Task<BoardCard> WaitForLaneAsync(int cardId, CardLane lane, TimeSpan? timeout = null)
	{
		return await WaitForCardAsync(cardId, card => card.Card.Lane == lane, $"card {cardId} to reach {lane}", timeout);
	}

	/// <summary>Waits until the pipeline stops a card: it is back at the top of the Queue in <see cref="CardState.Error"/>, waiting for a person.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="timeout">How long to wait; a minute unless given.</param>
	/// <returns>The stopped card, whose history summary says why.</returns>
	public async Task<BoardCard> WaitForStoppedAsync(int cardId, TimeSpan? timeout = null)
	{
		return await WaitForCardAsync(cardId, card => card.Card is { Lane: CardLane.Queue, State: CardState.Error }, $"card {cardId} to be stopped in Error", timeout);
	}

	/// <summary>Stops the pipeline, shuts the orchestrator down (killing running agents), and deletes the workspace root with the databases and the project folder. The source the test imported is not touched.</summary>
	/// <returns>A task that completes when everything is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			await StopOrchestratorAsync();
		}
		finally
		{
			await _provider.DisposeAsync();

			if (_clone is not null)
			{
				await _clone.DisposeAsync();
			}

			await TempFolder.DeleteAsync(_folder);
		}
	}

	private async Task<string> DescribeBoardAsync()
	{
		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"Pipeline {(Controller.IsRunning ? "started" : "stopped")}, repository {(Repository is null ? "not connected" : "connected")}.").AppendLine();
		if (OpenProject.Current is null)
		{
			return text.Append("No project is open.").ToString();
		}

		IReadOnlyList<BoardCard> board = await GetBoardAsync();
		if (board.Count == 0)
		{
			return text.Append("The board is empty.").ToString();
		}

		foreach (BoardCard card in board)
		{
			WorkCard work = card.Card;
			text.Append(CultureInfo.InvariantCulture, $"Card {work.Id} '{work.Name}': {work.Lane}, {work.State}, attempt {work.Attempt} of {work.AttemptLimit}");
			text.AppendLine(card.History.StopReason is null ? "." : $". {card.History.StopReason}");

			foreach (CardRound round in await GetHistoryAsync(work.Id))
			{
				string outcome = round.Outcome?.ToString() ?? "running";
				text.Append(CultureInfo.InvariantCulture, $"  Round {round.Sequence}: {round.Kind}, attempt {round.Attempt}, {outcome}");
				text.AppendLine(round.Note is null ? string.Empty : $" - {round.Note}");

				if (round.Report is { Length: > 0 } report)
				{
					string excerpt = report.Length > 400 ? report[..400] + "..." : report;
					text.Append("    ").AppendLine(excerpt.ReplaceLineEndings(" | "));
				}
			}
		}

		return text.ToString().TrimEnd();
	}
}
