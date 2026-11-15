using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>A running sandbox: commands run in it as its unprivileged user and see only what its <see cref="SandboxSpec"/> gave it. A command that times out or is stopped spoils the sandbox, because what it left running inside cannot be trusted to stop; disposing a spoiled sandbox, or one that is not kept, removes it. A kept sandbox's volumes always survive.</summary>
public interface ISandbox : IAsyncDisposable
{
	/// <summary>Gets the sandbox's name.</summary>
	string Name { get; }

	/// <summary>Gets the host folder Pyxie writes the round's input into; the sandbox sees it read-only at <see cref="InputPath"/>.</summary>
	string InputFolder { get; }

	/// <summary>Gets the host folder the round's output comes back in; the sandbox sees it at <see cref="OutputPath"/>.</summary>
	string OutputFolder { get; }

	/// <summary>Gets the input folder as commands in the sandbox see it.</summary>
	string InputPath { get; }

	/// <summary>Gets the output folder as commands in the sandbox see it.</summary>
	string OutputPath { get; }

	/// <summary>Gets the sandbox's own working folder, where the card's repository lives, as commands in the sandbox see it.</summary>
	string WorkPath { get; }

	/// <summary>Gets a value indicating whether a command was stopped part-way, so the sandbox must be removed rather than used again.</summary>
	bool IsSpoiled { get; }

	/// <summary>Runs a command in the sandbox and waits for it to end.</summary>
	/// <param name="command">The command.</param>
	/// <param name="cancellationToken">A token that stops the command, and spoils the sandbox, when cancelled.</param>
	/// <returns>How the run ended and its output. Starting failures and timeouts are results, not exceptions; a timeout spoils the sandbox.</returns>
	/// <exception cref="OperationCanceledException">The token was cancelled.</exception>
	/// <exception cref="SandboxUnavailableException">The container engine stopped answering.</exception>
	Task<ProcessResult> RunAsync(SandboxCommand command, CancellationToken cancellationToken);

	/// <summary>Writes a text file in the sandbox, as its unprivileged user, creating its folder.</summary>
	/// <param name="path">The file, as the sandbox sees it.</param>
	/// <param name="content">The text, written as UTF-8 without a byte order mark.</param>
	/// <param name="cancellationToken">A token to cancel the write.</param>
	/// <returns>A task that completes when the file is written.</returns>
	/// <exception cref="SandboxException">The file could not be written.</exception>
	Task WriteFileAsync(string path, string content, CancellationToken cancellationToken);

	/// <summary>Reads the beginning of a text file in the sandbox.</summary>
	/// <param name="path">The file, as the sandbox sees it.</param>
	/// <param name="maxLength">The most characters to read.</param>
	/// <param name="cancellationToken">A token to cancel the read.</param>
	/// <returns>The text, or <see langword="null"/> when there is no such file.</returns>
	Task<string?> ReadFileAsync(string path, int maxLength, CancellationToken cancellationToken);

	/// <summary>Ends every process the sandbox's unprivileged user still has running, such as a server an agent left behind, so nothing changes the sandbox while Pyxie reads it.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when they are gone.</returns>
	Task EndProcessesAsync(CancellationToken cancellationToken);

	/// <summary>Deletes a file in the sandbox, if it is there.</summary>
	/// <param name="path">The file, as the sandbox sees it.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the file is gone.</returns>
	Task DeleteFileAsync(string path, CancellationToken cancellationToken);
}
