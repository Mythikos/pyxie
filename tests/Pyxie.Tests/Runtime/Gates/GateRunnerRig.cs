using System.Text.Json;

using Pyxie.Data.Entities;
using Pyxie.Runtime.Gates;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Harnesses;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Gates;

/// <summary>A <see cref="GateRunner"/> with a card's sandbox (see <see cref="RoundSandbox"/>), a round log on the host, and a record of every gate that started. Gates are fake harness commands that note their name in the record, so a test can tell which gates ran, in what order, and where. The record and the log sit outside the sandbox. Disposing the rig deletes all of it.</summary>
public sealed class GateRunnerRig : IAsyncDisposable
{
	private const string RecordFile = "started.jsonl";
	private static readonly TimeSpan s_startWait = TimeSpan.FromSeconds(30);

	private readonly RoundSandbox _card;
	private readonly string _recordPath;

	private GateRunnerRig(RoundSandbox card)
	{
		_card = card;
		_recordPath = card.Combine(RecordFile);
		Directory.CreateDirectory(card.RepositoryFolder);
	}

	/// <summary>Gets the card's gate sandbox.</summary>
	public ISandbox Sandbox => _card.Sandbox;

	/// <summary>Gets the card's repository on the host, where the gates run.</summary>
	public string RepositoryFolder => _card.RepositoryFolder;

	/// <summary>Gets the host file the gates' full output is kept in. Its folder does not exist until a gate runs.</summary>
	public string LogPath => _card.LogPath;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Creates a rig with a started sandbox and the card's repository checked out in it (an empty folder: the gates the tests run need nothing in it).</summary>
	/// <param name="clock">The clock the sandbox's commands measure timeouts with; the system clock unless given.</param>
	/// <returns>The rig.</returns>
	public static async Task<GateRunnerRig> CreateAsync(TimeProvider? clock = null)
	{
		return new GateRunnerRig(await RoundSandbox.CreateAsync("pyxie-gate", clock));
	}

	/// <summary>Creates a gate that runs the fake harness, notes its name in the record when it starts, and then behaves as the arguments say.</summary>
	/// <param name="name">The gate's name, which is also what the record keeps.</param>
	/// <param name="fakeArguments">The fake harness's behavior arguments, each one argument. With none, the gate prints a line and exits with code 0.</param>
	/// <param name="timeoutMinutes">The gate's timeout in whole minutes.</param>
	/// <returns>The gate, enabled.</returns>
	public Gate CreateGate(string name, string[]? fakeArguments = null, int timeoutMinutes = 5)
	{
		string[] arguments = [.. fakeArguments ?? [], "--record", _recordPath, "--note", name];

		return new Gate { Name = name, Command = FakeHarnessAgent.Command, Arguments = FakeHarnessAgent.Arguments(arguments), TimeoutMinutes = timeoutMinutes, IsEnabled = true };
	}

	/// <summary>Creates a gate whose command does not exist in the sandbox, so it cannot start.</summary>
	/// <param name="name">The gate's name.</param>
	/// <returns>The gate.</returns>
	public static Gate CreateGateThatCannotStart(string name)
	{
		return new Gate { Name = name, Command = "pyxie-no-such-command-for-tests", Arguments = string.Empty, TimeoutMinutes = 5, IsEnabled = true };
	}

	/// <summary>Creates a gate that fails without printing anything: git asked for a setting an empty configuration file does not have exits with code 1 and says nothing.</summary>
	/// <param name="name">The gate's name.</param>
	/// <returns>The gate.</returns>
	public async Task<Gate> CreateSilentFailingGateAsync(string name)
	{
		string configuration = _card.Combine("empty.gitconfig");
		await File.WriteAllTextAsync(configuration, string.Empty, Token);

		return new Gate { Name = name, Command = "git", Arguments = $"config --file \"{configuration}\" --get pyxie.missing", TimeoutMinutes = 5, IsEnabled = true };
	}

	/// <summary>Runs gates in the card's repository in the sandbox.</summary>
	/// <param name="gates">The gates, in the order they run.</param>
	/// <returns>The verdict.</returns>
	public async Task<RoundVerdict> RunAsync(params Gate[] gates)
	{
		return await RunAsync(gates, Token);
	}

	/// <summary>Runs gates in the card's repository in the sandbox with a token the test controls.</summary>
	/// <param name="gates">The gates, in the order they run.</param>
	/// <param name="cancellationToken">A token that stops the running gate.</param>
	/// <returns>The verdict.</returns>
	public async Task<RoundVerdict> RunAsync(IReadOnlyList<Gate> gates, CancellationToken cancellationToken)
	{
		return await new GateRunner().RunAsync(gates, _card.Sandbox, _card.RepositoryPath, _card.LogPath, cancellationToken);
	}

	/// <summary>Gets the gates that started, in the order they started, with the folder each ran in.</summary>
	/// <returns>The names noted by <see cref="CreateGate"/> and the working directories.</returns>
	public async Task<IReadOnlyList<(string Gate, string Directory)>> ReadStartedAsync()
	{
		if (!File.Exists(_recordPath))
		{
			return [];
		}

		await using FileStream stream = new(_recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using StreamReader reader = new(stream);
		List<(string Gate, string Directory)> started = [];

		while (await reader.ReadLineAsync(Token) is string line)
		{
			using JsonDocument document = JsonDocument.Parse(line);
			string[] arguments = [.. document.RootElement.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString() ?? string.Empty)];
			int note = Array.IndexOf(arguments, "--note");
			started.Add((arguments[note + 1], document.RootElement.GetProperty("directory").GetString() ?? string.Empty));
		}

		return started;
	}

	/// <summary>Waits until a gate has started, so a test can cancel or time out a gate that is really running.</summary>
	/// <returns>A task that completes when at least one gate started. The test fails after thirty seconds.</returns>
	public async Task WaitForFirstStartAsync()
	{
		using CancellationTokenSource expired = CancellationTokenSource.CreateLinkedTokenSource(Token);
		expired.CancelAfter(s_startWait);

		try
		{
			while ((await ReadStartedAsync()).Count == 0)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(50), expired.Token);
			}
		}
		catch (OperationCanceledException) when (expired.IsCancellationRequested && !Token.IsCancellationRequested)
		{
			Assert.Fail($"No gate started within {s_startWait.TotalSeconds:0} seconds.");
		}
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return _card.DisposeAsync();
	}
}
