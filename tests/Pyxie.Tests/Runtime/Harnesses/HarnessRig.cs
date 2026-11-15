using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary>Runs agent rounds through <see cref="HarnessRunner"/> the way the pipeline does: in the card's sandbox of a <see cref="RoundSandbox"/>, against the card's repository as the sandbox sees it, with the round log on the host. Disposing the rig ends every process it started and deletes everything.</summary>
public sealed class HarnessRig : IAsyncDisposable
{
	/// <summary>The briefing a round gets unless a test supplies one.</summary>
	public const string DefaultBriefing = "# Card\n\nDo the task.";

	private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromMinutes(2);

	private readonly RoundSandbox _card;

	private HarnessRig(RoundSandbox card)
	{
		_card = card;
	}

	/// <summary>Gets the rig's own folder, outside the sandbox, for record files and scripts.</summary>
	public string Folder => _card.Folder;

	/// <summary>Gets the card's sandbox the rounds run in.</summary>
	public ISandbox Sandbox => _card.Sandbox;

	/// <summary>Gets the card's repository as the sandbox sees it, which is what the runner is given.</summary>
	public string RepositoryPath => _card.RepositoryPath;

	/// <summary>Gets the card's repository on the host. It does not exist until the runner (or the test) writes into it.</summary>
	public string RepositoryFolder => _card.RepositoryFolder;

	/// <summary>Gets the host file the agent's full console output is kept in. Its folder does not exist until an agent runs.</summary>
	public string LogPath => _card.LogPath;

	/// <summary>Gets where the agent's result file is on the host.</summary>
	public string ResultFile => Path.Combine(RepositoryFolder, PipelineContract.ResultFile);

	/// <summary>Gets where the round's briefing is on the host.</summary>
	public string BriefingFile => Path.Combine(RepositoryFolder, PipelineContract.BriefingFile);

	/// <summary>Gets where Pyxie's folder in the repository is on the host.</summary>
	public string PyxieFolder => Path.GetDirectoryName(BriefingFile) ?? RepositoryFolder;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Creates a rig with a started sandbox and no repository folder yet.</summary>
	/// <param name="clock">The clock the sandbox's commands measure their timeouts with; the system clock unless given.</param>
	/// <returns>The rig.</returns>
	public static async Task<HarnessRig> CreateAsync(TimeProvider? clock = null)
	{
		return new HarnessRig(await RoundSandbox.CreateAsync("pyxie-hrn", clock));
	}

	/// <summary>Creates a round for an agent, as the pipeline hands it to the runner.</summary>
	/// <param name="agent">The agent the round recorded, if any.</param>
	/// <param name="kind">The kind of round.</param>
	/// <param name="briefing">The briefing the round recorded, if any.</param>
	/// <returns>The round.</returns>
	public static PipelineWork Work(AgentSnapshot? agent, RoundKind kind = RoundKind.Work, string? briefing = DefaultBriefing)
	{
		return new PipelineWork(RoundSandbox.CardId, CardName: "Card", RoundId: 1, Sequence: 1, kind, Attempt: 1, agent, briefing);
	}

	/// <summary>Gets the full path of something under the rig's folder, outside the sandbox; it need not exist.</summary>
	/// <param name="relativePath">The path from the rig's folder.</param>
	/// <returns>The full path.</returns>
	public string Combine(string relativePath)
	{
		return _card.Combine(relativePath);
	}

	/// <summary>Writes a file into the card's repository through the sandbox, as an earlier round would have left it.</summary>
	/// <param name="relativePath">The path in the repository, with forward slashes.</param>
	/// <param name="content">The text.</param>
	/// <returns>A task that completes when the file is written.</returns>
	public async Task WriteRepositoryFileAsync(string relativePath, string content)
	{
		await Sandbox.WriteFileAsync(SandboxPaths.Combine(RepositoryPath, relativePath), content, Token);
	}

	/// <summary>Runs a round with a timeout of two minutes and the test's token.</summary>
	/// <param name="agent">The agent the round recorded, if any.</param>
	/// <param name="kind">The kind of round.</param>
	/// <param name="briefing">The briefing the round recorded, if any.</param>
	/// <returns>What the runner decided the round came to. The test fails when the runner does not finish within ninety seconds.</returns>
	public async Task<RoundVerdict> RunAsync(AgentSnapshot? agent, RoundKind kind = RoundKind.Work, string? briefing = DefaultBriefing)
	{
		return await StartAsync(Work(agent, kind, briefing), s_defaultTimeout, Token).WithinLimitAsync();
	}

	/// <summary>Starts a round the test controls: its timeout, its token, and the sandbox it goes through.</summary>
	/// <param name="work">The round.</param>
	/// <param name="timeout">How long the agent may run.</param>
	/// <param name="cancellationToken">A token that stops the agent.</param>
	/// <param name="through">The sandbox the runner is handed; the rig's own unless given, such as a <see cref="RecordingSandbox"/> that wraps it.</param>
	/// <returns>The running round.</returns>
	public Task<RoundVerdict> StartAsync(PipelineWork work, TimeSpan timeout, CancellationToken cancellationToken, ISandbox? through = null)
	{
		return new HarnessRunner().RunAsync(work, through ?? Sandbox, RepositoryPath, LogPath, timeout, cancellationToken);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return _card.DisposeAsync();
	}
}
