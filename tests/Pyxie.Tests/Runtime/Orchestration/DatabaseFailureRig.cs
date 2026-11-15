using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Sqlite.DependencyInjection;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Runtime.DependencyInjection;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>The runtime wired the way <see cref="RuntimeHarness"/> wires it, with a project database context factory a test can make fail (<see cref="Factory"/>) and with every log message captured (<see cref="Logs"/>). The test sets the pipeline up while the database works, makes it fail, and checks that the pipeline survives and recovers. State is read through <see cref="ReadRoundsAsync"/> and <see cref="ReadLaneAsync"/>, which bypass the failing factory. One worker, a one-second poll, and a private workspace root.</summary>
public sealed class DatabaseFailureRig : IAsyncDisposable
{
	private static readonly TimeSpan s_defaultWait = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(100);

	private readonly string _folder;
	private readonly IDbContextFactory<ProjectDbContext> _database;
	private readonly ServiceProvider _provider;
	private bool _started;
	private bool _disposed;

	/// <summary>Initializes a new instance of the <see cref="DatabaseFailureRig"/> class. Nothing runs until <see cref="StartOrchestratorAsync"/>.</summary>
	public DatabaseFailureRig()
	{
		_folder = TempFolder.Create("pyxie-dbf");
		OpenProject openProject = new();
		_database = new SqliteProjectDbContextFactory(openProject);
		Factory = new SwitchableContextFactory(_database);
		Logs = new CapturingLoggerProvider();
		WorkspaceRoot = Path.Combine(_folder, "ws");
		ScriptsFolder = Path.Combine(_folder, "scripts");
		Directory.CreateDirectory(ScriptsFolder);

		Dictionary<string, string?> settings = new()
		{
			["Workspace:Root"] = WorkspaceRoot,
			["Pipeline:Workers"] = "1",
			["Pipeline:PollIntervalSeconds"] = "1",
			["Pipeline:RoundTimeoutMinutes"] = "5",
		};

		IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		ServiceCollection services = new();
		services.AddLogging(builder => builder.AddProvider(Logs));
		services.AddSingleton(openProject);
		services.AddPyxieSqlite(provider => provider.GetRequiredService<WorkspaceLayout>().MachineDatabasePath);
		services.Replace(ServiceDescriptor.Singleton<IDbContextFactory<ProjectDbContext>>(Factory));
		services.AddPyxieData();
		services.AddPyxieRuntime(configuration);
		services.AddLocalSandboxes(Path.Combine(_folder, "sandboxes"));
		_provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		Directory.CreateDirectory(WorkspaceRoot);
		using (MachineDbContext machine = _provider.GetRequiredService<IDbContextFactory<MachineDbContext>>().CreateDbContext())
		{
			machine.Database.Migrate();
		}

		Orchestrator = _provider.GetServices<IHostedService>().OfType<PipelineOrchestrator>().Single();
		Controller = _provider.GetRequiredService<PipelineController>();
	}

	/// <summary>Gets the factory every service creates its contexts from, which a test makes fail.</summary>
	public SwitchableContextFactory Factory { get; }

	/// <summary>Gets what the runtime logged.</summary>
	public CapturingLoggerProvider Logs { get; }

	/// <summary>Gets the real orchestrator, whose <see cref="BackgroundService.ExecuteTask"/> ends only if the pipeline stopped the host.</summary>
	public PipelineOrchestrator Orchestrator { get; }

	/// <summary>Gets the controller that starts and stops the pipeline.</summary>
	public PipelineController Controller { get; }

	/// <summary>Gets the folder that holds the machine database and the project folders.</summary>
	public string WorkspaceRoot { get; }

	/// <summary>Gets a folder for fake-agent scripts.</summary>
	public string ScriptsFolder { get; }

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Imports a repository as a new open project, which needs the database to work.</summary>
	/// <param name="source">The repository to import.</param>
	/// <returns>A task that completes when the project is open and its repository connected.</returns>
	public async Task ConnectRepositoryAsync(TempGitRepository source)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();
		ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ProjectSetupService>().ImportFolderAsync(source.Folder, Token));
	}

	/// <summary>Adds a fake agent that can work.</summary>
	/// <param name="name">The unique agent name.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument.</param>
	/// <returns>The stored agent.</returns>
	public async Task<Agent> AddWorkerAsync(string name, params string[] fakeArguments)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<IAgentService>().CreateAsync(FakeHarnessAgent.Input(name, HarnessKind.Other, canWork: true, canReview: false, fakeArguments), Token));
	}

	/// <summary>Adds a card to the Queue.</summary>
	/// <param name="name">The card name.</param>
	/// <param name="worker">The worker agent.</param>
	/// <returns>The created card.</returns>
	public async Task<WorkCard> AddCardAsync(string name, Agent worker)
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();

		return ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardService>().CreateAsync(TestCardInput.Create(name, PyxieSeedData.FeatureCategoryId, worker.Id), Token));
	}

	/// <summary>Claims the next card and leaves its round open, the way a Pyxie that crashed would have left it.</summary>
	/// <returns>The open round.</returns>
	public async Task<PipelineWork> ClaimAsync()
	{
		await using AsyncServiceScope scope = _provider.CreateAsyncScope();
		PipelineWork? work = await scope.ServiceProvider.GetRequiredService<ICardPipelineService>().ClaimNextAsync(Token);
		Assert.NotNull(work);

		return work;
	}

	/// <summary>Starts the orchestrator, as Pyxie does at launch. The pipeline stays stopped until <see cref="Controller"/> starts it.</summary>
	/// <returns>A task that completes when the host has started the orchestrator. It throws when the orchestrator ended while starting, which is what a failure it did not survive looks like.</returns>
	public async Task StartOrchestratorAsync()
	{
		_started = true;
		await Orchestrator.StartAsync(Token);
	}

	/// <summary>Reads a card's rounds straight from the database, whether or not the factory is failing.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The rounds, oldest first.</returns>
	public async Task<IReadOnlyList<CardRound>> ReadRoundsAsync(int cardId)
	{
		await using ProjectDbContext context = await _database.CreateDbContextAsync(Token);

		return await context.Rounds.AsNoTracking().Where(round => round.CardId == cardId).OrderBy(round => round.Sequence).ToListAsync(Token);
	}

	/// <summary>Reads a card's lane and state straight from the database, whether or not the factory is failing.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The lane and the state.</returns>
	public async Task<(CardLane Lane, CardState State)> ReadLaneAsync(int cardId)
	{
		await using ProjectDbContext context = await _database.CreateDbContextAsync(Token);
		WorkCard card = await context.Cards.AsNoTracking().SingleAsync(existing => existing.Id == cardId, Token);

		return (card.Lane, card.State);
	}

	/// <summary>Waits until a condition holds, checking every tenth of a second.</summary>
	/// <param name="condition">The condition. It must not throw while it is false.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ..." in the failure message.</param>
	/// <param name="timeout">How long to wait; a minute unless given.</param>
	/// <returns>A task that completes when the condition holds. The test fails when it does not in time.</returns>
	public async Task WaitUntilAsync(Func<Task<bool>> condition, string description, TimeSpan? timeout = null)
	{
		TimeSpan limit = timeout ?? s_defaultWait;
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!await condition())
		{
			Assert.True(stopwatch.Elapsed < limit, $"Timed out waiting for {description} after {limit.TotalSeconds:0} seconds. Logged: {string.Join(" | ", Logs.Entries.Select(entry => $"{entry.Level}: {entry.Message}"))}");
			await Task.Delay(s_pollInterval, Token);
		}
	}

	/// <summary>Stops the pipeline and the orchestrator (which kills running agents), and deletes the workspace root with the databases.</summary>
	/// <returns>A task that completes when everything is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Factory.Failing = false;
		Factory.FailNext(0);
		Controller.Stop();

		try
		{
			if (_started)
			{
				using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
				await Orchestrator.StopAsync(timeout.Token);
			}
		}
		finally
		{
			await _provider.DisposeAsync();
			await TempFolder.DeleteAsync(_folder);
		}
	}
}
