using System.Globalization;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Data.Sqlite.DependencyInjection;
using Pyxie.Runtime.DependencyInjection;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>The runtime wired the way <see cref="RuntimeHarness"/> wires it (the SQLite provider, the data services, <c>AddPyxieRuntime</c>, and <c>AddLocalSandboxes</c>), except that its sandboxes are <see cref="SwitchableSandboxes"/> over the local stand-ins, so a test can make them fail the way Docker does. A test either drives a <see cref="RoundExecutor"/> resolved from a scope (<see cref="RunRoundAsync"/>, with the pipeline stopped) or starts the real <see cref="PipelineOrchestrator"/>. One worker, a one-second poll, a one-minute round timeout, and a private workspace root, which disposing the rig deletes with everything in it.</summary>
public sealed class SandboxRig : IAsyncDisposable
{
	private static readonly TimeSpan s_stopTimeout = TimeSpan.FromSeconds(30);

	// Activity wakes a wait at once; this bounds how long a change that does not announce itself (the pipeline stopping, a file appearing) goes unnoticed.
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(100);

	private readonly string _folder;
	private readonly ServiceProvider _provider;
	private readonly PipelineOrchestrator _orchestrator;
	private TempGitRepository? _hub;
	private bool _orchestratorStarted;
	private bool _orchestratorStopped;
	private bool _disposed;

	/// <summary>Initializes a new instance of the <see cref="SandboxRig"/> class: builds the services and the machine database, with no project. Nothing runs until a test asks.</summary>
	public SandboxRig()
	{
		_folder = TempFolder.Create("pyxie-sbx");
		string workspaceRoot = Path.Combine(_folder, "ws");
		string sandboxes = Path.Combine(_folder, "sandboxes");
		MarkerFolder = Path.Combine(_folder, "marks");
		Directory.CreateDirectory(MarkerFolder);

		Dictionary<string, string?> settings = new()
		{
			["Workspace:Root"] = workspaceRoot,
			["Pipeline:Workers"] = "1",
			["Pipeline:PollIntervalSeconds"] = "1",
			["Pipeline:RoundTimeoutMinutes"] = "1",
		};

		IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		ServiceCollection services = new();
		services.AddLogging();
		services.AddPyxieSqlite(provider => provider.GetRequiredService<WorkspaceLayout>().MachineDatabasePath);
		services.AddPyxieData();
		services.AddPyxieRuntime(configuration);
		services.AddLocalSandboxes(sandboxes);
		services.Replace(ServiceDescriptor.Singleton<ISandboxes>(provider => new SwitchableSandboxes(new LocalSandboxes(provider.GetRequiredService<ProcessRunner>(), sandboxes))));
		_provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		Directory.CreateDirectory(workspaceRoot);
		using (MachineDbContext machine = _provider.GetRequiredService<IDbContextFactory<MachineDbContext>>().CreateDbContext())
		{
			machine.Database.Migrate();
		}

		_orchestrator = _provider.GetServices<IHostedService>().OfType<PipelineOrchestrator>().Single();
		Sandboxes = (SwitchableSandboxes)_provider.GetRequiredService<ISandboxes>();
		Controller = _provider.GetRequiredService<PipelineController>();
		Activity = _provider.GetRequiredService<PipelineActivity>();
		OpenProject = _provider.GetRequiredService<OpenProject>();
	}

	/// <summary>Gets the sandboxes every round runs in, which a test makes fail.</summary>
	public SwitchableSandboxes Sandboxes { get; }

	/// <summary>Gets the controller that starts and stops the pipeline and says why it stopped by itself.</summary>
	public PipelineController Controller { get; }

	/// <summary>Gets the signal that cards changed.</summary>
	public PipelineActivity Activity { get; }

	/// <summary>Gets the open project, which <see cref="ConnectRepositoryAsync"/> opens.</summary>
	public OpenProject OpenProject { get; }

	/// <summary>Gets a folder for files fake agents and gates write to mark what ran, outside every repository and sandbox, so nothing a round does removes them.</summary>
	public string MarkerFolder { get; }

	/// <summary>Gets the open project's folder, which holds its database, its hub, its sandboxes' host folders, and the round logs.</summary>
	/// <exception cref="InvalidOperationException">No repository is connected yet, so no project is open.</exception>
	public string ProjectFolder => OpenProject.Current?.Folder ?? throw new InvalidOperationException($"No project is open: call {nameof(ConnectRepositoryAsync)} first.");

	/// <summary>Gets the connected repository as the repository service stored it, or <see langword="null"/> before <see cref="ConnectRepositoryAsync"/>. Its <c>Path</c> is the hub.</summary>
	public Repository? Repository { get; private set; }

	/// <summary>Gets the project's hub, the bare repository the pipeline takes work back into and merges in. The rig owns the folder and deletes it with the workspace root.</summary>
	/// <exception cref="InvalidOperationException">No repository is connected yet.</exception>
	public TempGitRepository Hub => _hub ?? throw new InvalidOperationException($"No repository is connected: call {nameof(ConnectRepositoryAsync)} first.");

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Imports a repository the way a person does, which creates and opens a project whose hub is a bare clone of it, with <c>main</c> as the target branch.</summary>
	/// <param name="source">The repository to import. It stays owned by the test, which declares it before the rig so it is deleted last.</param>
	/// <returns>The connected repository. The test fails when the import is refused.</returns>
	public async Task<Repository> ConnectRepositoryAsync(TempGitRepository source)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();
		ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ProjectSetupService>().ImportFolderAsync(source.Folder, Token));
		Repository connected = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().GetAsync(Token) ?? throw new InvalidOperationException("The import succeeded but no repository is stored.");
		Repository = connected;
		_hub = await TempGitRepository.AttachAsync(connected.Path);
		Activity.NotifyCardsChanged();

		return connected;
	}

	/// <summary>Deletes the open project's repository row, leaving the project open without a repository, a state the executor must refuse to run rounds in.</summary>
	/// <returns>A task that completes when the row is gone.</returns>
	public async Task RemoveRepositoryRowAsync()
	{
		await using ProjectDbContext context = await _provider.GetRequiredService<IDbContextFactory<ProjectDbContext>>().CreateDbContextAsync(Token);
		await context.Repositories.ExecuteDeleteAsync(Token);
	}

	/// <summary>Adds a gate after the gates that exist.</summary>
	/// <param name="input">The gate.</param>
	/// <returns>The stored gate. The test fails when the service refuses it.</returns>
	public async Task<Gate> AddGateAsync(GateInput input)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<IGateService>().CreateAsync(input, Token));
	}

	/// <summary>Adds an enabled gate that runs the fake harness with the given behavior arguments and a five-minute timeout.</summary>
	/// <param name="name">The unique gate name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument.</param>
	/// <returns>The stored gate.</returns>
	public async Task<Gate> AddGateAsync(string name, params string[] fakeArguments)
	{
		return await AddGateAsync(new GateInput(name, FakeHarnessAgent.Command, FakeHarnessAgent.Arguments(HarnessKind.Other, fakeArguments), TimeoutMinutes: 5, IsEnabled: true));
	}

	/// <summary>Adds a fake agent that can work, with the <see cref="HarnessKind.Other"/> harness.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument.</param>
	/// <returns>The stored agent. The test fails when the service refuses it.</returns>
	public async Task<Agent> AddWorkerAsync(string name, params string[] fakeArguments)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<IAgentService>().CreateAsync(FakeHarnessAgent.Input(name, HarnessKind.Other, canWork: true, canReview: false, fakeArguments), Token));
	}

	/// <summary>Adds a card with no reviewer to the bottom of the Queue, as a person would, and wakes idle pipeline workers.</summary>
	/// <param name="name">The card name.</param>
	/// <param name="worker">The worker agent.</param>
	/// <returns>The created card. The test fails when the service refuses it.</returns>
	public async Task<WorkCard> AddCardAsync(string name, Agent worker)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();
		WorkCard card = ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().CreateAsync(TestCardInput.Create(name, PyxieSeedData.FeatureCategoryId, worker.Id), Token));
		Activity.NotifyCardsChanged();

		return card;
	}

	/// <summary>Runs one round with a <see cref="RoundExecutor"/> resolved from a scope of its own, the way the orchestrator runs each round.</summary>
	/// <param name="work">The round, as the pipeline would have opened it. The executor never reads the card itself.</param>
	/// <param name="cancellationToken">A token that stops the round when cancelled.</param>
	/// <returns>The verdict and the repository the round ran against.</returns>
	public async Task<(RoundVerdict Verdict, Repository? Repository)> RunRoundAsync(PipelineWork work, CancellationToken cancellationToken)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return await scope.ServiceProvider.GetRequiredService<RoundExecutor>().RunAsync(work, cancellationToken);
	}

	/// <summary>Gets the folder that stands for a sandbox's working folder: a worker's is kept between the card's work rounds; any other is deleted when its round ends.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="role">What the sandbox is for.</param>
	/// <returns>The full path.</returns>
	public string SandboxWorkFolder(int cardId, SandboxRole role)
	{
		return Sandboxes.Local.WorkFolder(SandboxNames.Container(SandboxNames.ProjectKey(ProjectFolder), cardId, role));
	}

	/// <summary>Gets the card's repository in its worker sandbox, where the worker works and Pyxie commits what it left.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The full path.</returns>
	public string WorkerRepository(int cardId)
	{
		return Path.Combine(SandboxWorkFolder(cardId, SandboxRole.Worker), "repo");
	}

	/// <summary>Starts the orchestrator if needed, as Pyxie does at launch, and then the pipeline, which first checks that sandboxes can be made.</summary>
	/// <returns>A task that completes when the pipeline is started. The test fails when the controller refuses to start.</returns>
	public async Task StartPipelineAsync()
	{
		if (!_orchestratorStarted)
		{
			_orchestratorStarted = true;
			await _orchestrator.StartAsync(Token);
		}

		ResultAssert.Succeeded(Controller.Start());
	}

	/// <summary>Shuts the orchestrator down, as Pyxie does when it exits. Does nothing when it never started or already stopped.</summary>
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

	/// <summary>Waits until a condition holds, checking at once, whenever cards change, and at least every tenth of a second.</summary>
	/// <param name="condition">The condition. It must not throw while it is false.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ...".</param>
	/// <returns>A task that completes when the condition holds. The test fails with the description and the board when it does not within <see cref="RuntimeTestTimeouts.Wait"/>.</returns>
	public async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
	{
		using CancellationTokenSource expired = CancellationTokenSource.CreateLinkedTokenSource(Token);
		expired.CancelAfter(RuntimeTestTimeouts.Wait);

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
			Assert.Fail($"Timed out waiting for {description} after {RuntimeTestTimeouts.Wait.TotalSeconds:0} seconds.{Environment.NewLine}{await DescribeBoardAsync()}");
		}
	}

	/// <summary>Waits until a card on the board satisfies a condition.</summary>
	/// <param name="cardId">The card. A card that is not on the board never satisfies it.</param>
	/// <param name="condition">The condition over the card and its history summary.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ...".</param>
	/// <returns>The card as it was when the condition held.</returns>
	public async Task<BoardCard> WaitForCardAsync(int cardId, Func<BoardCard, bool> condition, string description)
	{
		BoardCard? found = null;
		await WaitUntilAsync(
			async () =>
			{
				found = (await GetBoardAsync()).FirstOrDefault(card => card.Card.Id == cardId && condition(card));

				return found is not null;
			},
			description);

		return found ?? throw new InvalidOperationException($"Card {cardId} was not found although the wait for {description} completed.");
	}

	/// <summary>Waits until a card is in a lane.</summary>
	/// <param name="cardId">The card.</param>
	/// <param name="lane">The lane.</param>
	/// <returns>The card as it was when it reached the lane.</returns>
	public async Task<BoardCard> WaitForLaneAsync(int cardId, CardLane lane)
	{
		return await WaitForCardAsync(cardId, card => card.Card.Lane == lane, $"card {cardId} to reach {lane}");
	}

	/// <summary>Gets the board as people see it.</summary>
	/// <returns>The cards that are not closed, with their history summaries.</returns>
	public async Task<IReadOnlyList<BoardCard>> GetBoardAsync()
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return await scope.ServiceProvider.GetRequiredService<ICardService>().GetBoardAsync(Token);
	}

	/// <summary>Gets a card's rounds, oldest first.</summary>
	/// <param name="cardId">The card.</param>
	/// <returns>The rounds. The test fails when the card does not exist.</returns>
	public async Task<IReadOnlyList<CardRound>> GetHistoryAsync(int cardId)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().GetHistoryAsync(cardId, Token));
	}

	/// <summary>Stops the pipeline, shuts the orchestrator down, and deletes the workspace root, the sandboxes' folders, and the markers. The imported source is not touched.</summary>
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

			if (_hub is not null)
			{
				await _hub.DisposeAsync();
			}

			await TempFolder.DeleteAsync(_folder);
		}
	}

	private async Task<string> DescribeBoardAsync()
	{
		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"Pipeline {(Controller.IsRunning ? "started" : "stopped")}, stop reason {Controller.StopReason ?? "none"}.").AppendLine();
		if (OpenProject.Current is null)
		{
			return text.Append("No project is open.").ToString();
		}

		foreach (BoardCard card in await GetBoardAsync())
		{
			WorkCard work = card.Card;
			text.Append(CultureInfo.InvariantCulture, $"Card {work.Id} '{work.Name}': {work.Lane}, {work.State}, attempt {work.Attempt}.").AppendLine();

			foreach (CardRound round in await GetHistoryAsync(work.Id))
			{
				text.Append(CultureInfo.InvariantCulture, $"  Round {round.Sequence}: {round.Kind}, attempt {round.Attempt}, {round.Outcome?.ToString() ?? "running"}");
				text.AppendLine(round.Note is null ? string.Empty : $" - {round.Note}");
			}
		}

		return text.ToString().TrimEnd();
	}
}
