using Pyxie.Tests.Runtime.Processes;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>Holds a git command at a known point so a test can act while the command is running: a shell snippet for a git filter or hook that notes it started and then waits until the test releases it. It never sleeps for a fixed time, so the test decides exactly when the command may go on. Disposing it releases the command, so a failing test never leaves git blocked.</summary>
public sealed class GitBlocker : IDisposable
{
	/// <summary>Initializes a new instance of the <see cref="GitBlocker"/> class.</summary>
	/// <param name="folder">An existing folder outside the repository under test, so the two marker files are never part of a commit.</param>
	public GitBlocker(string folder)
	{
		StartedPath = Path.Combine(folder, "blocker-started");
		ReleasePath = Path.Combine(folder, "blocker-release");
	}

	/// <summary>Gets the file the snippet creates when git reaches it.</summary>
	public string StartedPath { get; }

	/// <summary>Gets the file whose existence lets the snippet finish.</summary>
	public string ReleasePath { get; }

	/// <summary>Gets the shell snippet: it creates <see cref="StartedPath"/> and waits for <see cref="ReleasePath"/>. git runs filters and hooks through its own <c>sh</c>, which accepts Windows paths with forward slashes. Append <c>; cat</c> for a filter, which must pass its input through, or <c>; exit 0</c> for a hook.</summary>
	public string Script => $"echo started > \"{Slashes(StartedPath)}\"; while [ ! -f \"{Slashes(ReleasePath)}\" ]; do sleep 0.05; done";

	/// <summary>Installs a hook in a repository that holds git at the snippet whenever git runs it. A hook of a repository applies to every worktree of it.</summary>
	/// <param name="gitFolder">The repository's own git folder (<see cref="Pyxie.Tests.Support.TempGitRepository.GitFolder"/>), whose <c>hooks</c> folder holds the hook.</param>
	/// <param name="hook">The hook's name, such as <c>post-commit</c>.</param>
	/// <param name="cancellationToken">A token to cancel the write.</param>
	/// <returns>A task that completes when the hook is installed.</returns>
	public async Task InstallHookAsync(string gitFolder, string hook, CancellationToken cancellationToken)
	{
		string path = Path.Combine(gitFolder, "hooks", hook);
		await File.WriteAllTextAsync(path, $"#!/bin/sh\n{Script}\nexit 0\n", cancellationToken);

		// git ignores a hook that is not executable, which is how every file starts outside Windows.
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}

	/// <summary>Waits until git has reached the snippet and is held there.</summary>
	/// <returns>A task that completes when the snippet started. The test fails when it does not within thirty seconds.</returns>
	public async Task WaitUntilBlockedAsync()
	{
		await ChildProcessProbe.WaitForFileAsync(StartedPath, "git to reach the blocking filter or hook");
	}

	/// <summary>Lets the held command go on.</summary>
	public void Release()
	{
		File.WriteAllText(ReleasePath, string.Empty);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Release();
	}

	private static string Slashes(string path)
	{
		return path.Replace('\\', '/');
	}
}
