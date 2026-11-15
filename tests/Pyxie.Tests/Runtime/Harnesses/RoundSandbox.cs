using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary>A card's sandbox for one test's rounds: a started <see cref="LocalSandbox"/> with the card's repository in it, as the sandbox sees it, and the round log on the host, all in a folder of the test's own. The repository folder does not exist until something writes into it, so a test can tell that a round touched nothing. Files a test keeps for itself, such as record files and scripts, belong under <see cref="Combine"/>, outside the sandbox. Disposing it ends every process its sandbox started and deletes everything.</summary>
/// <remarks>The sandbox has the worker's role whatever the round: the runners do not depend on the role, and a kept sandbox leaves its folder to this type, whose deletion retries while Windows still holds a folder that a killed process used.</remarks>
public sealed class RoundSandbox : IAsyncDisposable
{
	/// <summary>The card the sandbox works for.</summary>
	public const int CardId = 1;

	private const string RepositoryFolderName = "repo";

	private readonly ProcessRunner _processes;

	private RoundSandbox(string folder, ProcessRunner processes, ISandbox sandbox)
	{
		Folder = folder;
		_processes = processes;
		Sandbox = sandbox;
		RepositoryPath = SandboxPaths.Combine(sandbox.WorkPath, RepositoryFolderName);
		RepositoryFolder = Path.GetFullPath(RepositoryPath);
		LogPath = Path.Combine(folder, "logs", "round.log");
	}

	/// <summary>Gets the test's own folder, outside the sandbox, for record files and scripts.</summary>
	public string Folder { get; }

	/// <summary>Gets the card's sandbox.</summary>
	public ISandbox Sandbox { get; }

	/// <summary>Gets the card's repository as the sandbox sees it, which is what a runner is given.</summary>
	public string RepositoryPath { get; }

	/// <summary>Gets the card's repository on the host: the stand-in sandbox's folder for it.</summary>
	public string RepositoryFolder { get; }

	/// <summary>Gets the host file a round's full output is kept in. Its folder does not exist until a round runs.</summary>
	public string LogPath { get; }

	/// <summary>Creates the folder and starts the sandbox.</summary>
	/// <param name="prefix">What the folder is for, which starts its name.</param>
	/// <param name="clock">The clock the sandbox's commands measure their timeouts with; the system clock unless given.</param>
	/// <returns>The started sandbox.</returns>
	public static async Task<RoundSandbox> CreateAsync(string prefix, TimeProvider? clock = null)
	{
		string folder = TempFolder.Create(prefix);
		ProcessRunner processes = new(clock ?? TimeProvider.System);
		LocalSandboxes sandboxes = new(processes, Path.Combine(folder, "sandboxes"));
		string projectKey = SandboxNames.ProjectKey(folder);
		SandboxSpec spec = new(SandboxNames.Container(projectKey, CardId, SandboxRole.Worker), LocalSandboxes.Image, projectKey, CardId, SandboxRole.Worker, Path.Combine(folder, "in"), Path.Combine(folder, "out"));

		return new RoundSandbox(folder, processes, await sandboxes.StartAsync(spec, TestContext.Current.CancellationToken));
	}

	/// <summary>Gets the full path of something under the test's folder, outside the sandbox; it need not exist.</summary>
	/// <param name="relativePath">The path from the test's folder.</param>
	/// <returns>The full path.</returns>
	public string Combine(string relativePath)
	{
		return Path.Combine(Folder, relativePath);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Sandbox.DisposeAsync();
		_processes.Dispose();
		await TempFolder.DeleteAsync(Folder);
	}
}
