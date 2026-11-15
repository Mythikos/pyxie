using System.Text.Json;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>A <see cref="RoundExecutor"/> over a real hub and the local stand-in sandboxes (<see cref="SandboxRig"/>), driven the way the orchestrator drives it but without a pipeline: the test hands it one card's rounds one at a time and looks at the verdict, at the card's branch in the hub (<see cref="Hub"/>), and at the card's worker sandbox. The first round it runs is sequence 1, which starts the card afresh; later work rounds reuse the worker's sandbox. Disposing the rig deletes everything, the imported source too.</summary>
public sealed class RoundExecutorRig : IAsyncDisposable
{
	/// <summary>The card every round belongs to. The executor never reads the card, only the round it is given.</summary>
	public const int CardId = 1;

	/// <summary>The card's name, which is the subject of the commit Pyxie makes after a work round.</summary>
	public const string CardName = "Executor card";

	private int _sequence;

	private RoundExecutorRig(TempGitRepository source, SandboxRig runtime)
	{
		Source = source;
		Runtime = runtime;
	}

	/// <summary>Gets the repository that was imported. The executor never changes it.</summary>
	public TempGitRepository Source { get; }

	/// <summary>Gets the runtime the executor is resolved from, whose sandboxes a test can make fail. Its pipeline is never started.</summary>
	public SandboxRig Runtime { get; }

	/// <summary>Gets the project's hub, where the card's branch is and where the work comes back to. The test must have connected a repository.</summary>
	public TempGitRepository Hub => Runtime.Hub;

	/// <summary>Gets the card's branch.</summary>
	public static string Branch => PipelineContract.BranchName(CardId);

	/// <summary>Gets the card's repository in its worker sandbox, which is kept between the card's work rounds.</summary>
	public string WorkerRepository => Runtime.WorkerRepository(CardId);

	/// <summary>Gets a folder for files the fake agents and gates write to mark what ran, outside every repository and sandbox.</summary>
	public string MarkerFolder => Runtime.MarkerFolder;

	/// <summary>Gets the file the recording gates of <see cref="AddRecordingGateAsync"/> note themselves in.</summary>
	public string GateRecordPath => Path.Combine(MarkerFolder, "gates.jsonl");

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Creates the rig.</summary>
	/// <param name="connectRepository">Whether to import the repository, which opens its project, so rounds have somewhere to run. A rig without one shows how the executor refuses.</param>
	/// <param name="arrangeSource">Commits made in the source before it is imported, so the hub starts with them on <c>main</c>.</param>
	/// <returns>The rig.</returns>
	public static async Task<RoundExecutorRig> CreateAsync(bool connectRepository = true, Func<TempGitRepository, Task>? arrangeSource = null)
	{
		TempGitRepository source = await TempGitRepository.CreateAsync();
		SandboxRig runtime = new();

		try
		{
			if (arrangeSource is not null)
			{
				await arrangeSource(source);
			}

			if (connectRepository)
			{
				await runtime.ConnectRepositoryAsync(source);
			}

			return new RoundExecutorRig(source, runtime);
		}
		catch
		{
			await runtime.DisposeAsync();
			await source.DisposeAsync();

			throw;
		}
	}

	/// <summary>Opens the card's next round, as the pipeline would have opened it, without running it.</summary>
	/// <param name="kind">The kind of round.</param>
	/// <param name="agent">The agent of a work or review round, as the round would have recorded it.</param>
	/// <returns>The round, whose briefing is <c>Briefing of round N</c> for a round that runs an agent.</returns>
	public PipelineWork NextRound(RoundKind kind, AgentSnapshot? agent = null)
	{
		_sequence++;
		string? briefing = kind.RunsAgent() ? $"Briefing of round {_sequence}" : null;

		return new PipelineWork(CardId, CardName, RoundId: _sequence, Sequence: _sequence, kind, Attempt: 1, agent, briefing);
	}

	/// <summary>Runs the card's next round with the executor.</summary>
	/// <param name="kind">The kind of round.</param>
	/// <param name="agent">The agent of a work or review round, as the round would have recorded it.</param>
	/// <returns>The verdict and the repository the round ran against.</returns>
	public async Task<(RoundVerdict Verdict, Repository? Repository)> RunAsync(RoundKind kind, AgentSnapshot? agent = null)
	{
		return await Runtime.RunRoundAsync(NextRound(kind, agent), Token);
	}

	/// <summary>Runs a successful work round with a worker that writes <c>feature.txt</c>, so the card has a branch in the hub for gate, review, and merge rounds.</summary>
	/// <returns>The commit the card's branch points at afterwards.</returns>
	public async Task<string> WorkAsync()
	{
		(RoundVerdict verdict, _) = await RunAsync(RoundKind.Work, FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "feature.txt=hello"));
		Assert.True(verdict.Result == RoundResult.Succeeded, $"The work round that sets the card up must succeed: {verdict.Note}");

		return await GetBranchTipAsync();
	}

	/// <summary>Makes the card conflict with the target branch: a successful work round with a worker that rewrites <c>README.md</c>, and then the target branch moves on the way a person moves it, with a different version of the same file.</summary>
	/// <returns>The card branch's tip and the target branch's new tip, the commits a resolve round merges.</returns>
	public async Task<(string CardTip, string TargetTip)> ConflictAsync()
	{
		(RoundVerdict verdict, _) = await RunAsync(RoundKind.Work, FakeHarnessAgent.Snapshot("Worker", HarnessKind.Other, "--write", "README.md=the card's version"));
		Assert.True(verdict.Result == RoundResult.Succeeded, $"The work round that sets the card up must succeed: {verdict.Note}");
		string cardTip = await GetBranchTipAsync();

		return (cardTip, await MoveTargetAsync("README.md", "the target's version"));
	}

	/// <summary>Moves the target branch on the way a person does: clones the hub, commits one file, and pushes it back.</summary>
	/// <param name="relativePath">The file to commit.</param>
	/// <param name="content">Its text.</param>
	/// <returns>The target branch's new tip.</returns>
	public async Task<string> MoveTargetAsync(string relativePath, string content)
	{
		await using ClonedRepository person = await ClonedRepository.CreateAsync(Hub);
		await person.RunGitAsync("checkout", "--quiet", "-B", "main", "origin/main");
		string tip = await person.CommitFileAsync(relativePath, content);
		await person.PushAsync("main");

		return tip;
	}

	/// <summary>Gets the commit the card's branch points at in the hub.</summary>
	/// <returns>The full hash.</returns>
	public async Task<string> GetBranchTipAsync()
	{
		return await Hub.GetHashAsync(Branch);
	}

	/// <summary>Determines whether the card's branch is in the hub.</summary>
	/// <returns><see langword="true"/> when it is.</returns>
	public async Task<bool> BranchExistsAsync()
	{
		return (await Hub.TryRunGitAsync("rev-parse", "--verify", "--quiet", $"refs/heads/{Branch}")).Succeeded;
	}

	/// <summary>Gets what is uncommitted in the card's repository in its worker sandbox.</summary>
	/// <returns>The <c>git status --porcelain</c> lines; empty when it is clean (Pyxie's folder is excluded from git).</returns>
	public async Task<IReadOnlyList<string>> GetWorkerChangesAsync()
	{
		return await WorkingTreeStatus.ListAsync(WorkerRepository);
	}

	/// <summary>Reads the full console output a round kept in its log.</summary>
	/// <param name="sequence">The round's sequence.</param>
	/// <returns>The log's text.</returns>
	public async Task<string> ReadRoundLogAsync(int sequence)
	{
		return await File.ReadAllTextAsync(WorkspaceLayout.RoundLogPath(Runtime.ProjectFolder, CardId, sequence), Token);
	}

	/// <summary>Gets the specs of the sandboxes the card's rounds started, in order.</summary>
	/// <returns>The specs.</returns>
	public IReadOnlyList<SandboxSpec> StartedSandboxes()
	{
		return Runtime.Sandboxes.Local.Started;
	}

	/// <summary>Adds a gate that runs the fake harness and notes its name in <see cref="GateRecordPath"/> when it starts, then behaves as the arguments say.</summary>
	/// <param name="name">The unique gate name.</param>
	/// <param name="isEnabled">Whether gate rounds run the gate.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument.</param>
	/// <returns>The stored gate.</returns>
	public async Task<Gate> AddRecordingGateAsync(string name, bool isEnabled = true, params string[] fakeArguments)
	{
		string[] arguments = [.. fakeArguments, "--record", GateRecordPath, "--note", name];

		return await Runtime.AddGateAsync(new GateInput(name, FakeHarnessAgent.Command, FakeHarnessAgent.Arguments(HarnessKind.Other, arguments), TimeoutMinutes: 5, isEnabled));
	}

	/// <summary>Gets the names of the recording gates that started, in the order they started.</summary>
	/// <returns>The names; empty when none started.</returns>
	public async Task<IReadOnlyList<string>> ReadStartedGatesAsync()
	{
		if (!File.Exists(GateRecordPath))
		{
			return [];
		}

		List<string> names = [];
		foreach (string line in await File.ReadAllLinesAsync(GateRecordPath, Token))
		{
			using JsonDocument document = JsonDocument.Parse(line);
			string[] arguments = [.. document.RootElement.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString() ?? string.Empty)];
			names.Add(arguments[Array.IndexOf(arguments, "--note") + 1]);
		}

		return names;
	}

	/// <summary>Reads the folder a fake agent or gate that ran with <c>--record</c> ran in, from its only record.</summary>
	/// <param name="record">The record file.</param>
	/// <returns>The full path of the folder.</returns>
	public static async Task<string> ReadRecordedDirectoryAsync(string record)
	{
		using JsonDocument run = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(record, Token)));

		return Path.GetFullPath(run.RootElement.GetProperty("directory").GetString() ?? string.Empty);
	}

	/// <summary>Writes a hook into the card's repository in its worker sandbox, so it runs when Pyxie commits the worker's changes there. The worker's sandbox must exist, so run a work round first.</summary>
	/// <param name="hook">The hook's name, such as <c>pre-commit</c>.</param>
	/// <param name="script">The shell lines the hook runs, after <c>#!/bin/sh</c>.</param>
	/// <returns>A task that completes when the hook is in place.</returns>
	public async Task InstallWorkerHookAsync(string hook, string script)
	{
		string path = Path.Combine(WorkerRepository, ".git", "hooks", hook);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? WorkerRepository);
		await File.WriteAllTextAsync(path, $"#!/bin/sh\n{script}\n", Token);

		// git ignores a hook that is not executable, which is how every file starts outside Windows.
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Runtime.DisposeAsync();
		await Source.DisposeAsync();
	}
}
