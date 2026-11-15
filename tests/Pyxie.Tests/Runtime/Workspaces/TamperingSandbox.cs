using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A sandbox that runs everything in another one and, after each command that writes a git bundle, lets the test change what the command left in the output folder: the way an agent that controls its sandbox could hand out something other than the bundle Pyxie asked for. Disposing it leaves the inner sandbox to its owner.</summary>
public sealed class TamperingSandbox : ISandbox
{
	private readonly ISandbox _inner;
	private readonly Func<string, Task> _tamper;

	/// <summary>Initializes a new instance of the <see cref="TamperingSandbox"/> class.</summary>
	/// <param name="inner">The sandbox commands run in.</param>
	/// <param name="tamper">Runs after each <c>git bundle</c> command, with the host's output folder.</param>
	public TamperingSandbox(ISandbox inner, Func<string, Task> tamper)
	{
		_inner = inner;
		_tamper = tamper;
	}

	/// <inheritdoc/>
	public string Name => _inner.Name;

	/// <inheritdoc/>
	public string InputFolder => _inner.InputFolder;

	/// <inheritdoc/>
	public string OutputFolder => _inner.OutputFolder;

	/// <inheritdoc/>
	public string InputPath => _inner.InputPath;

	/// <inheritdoc/>
	public string OutputPath => _inner.OutputPath;

	/// <inheritdoc/>
	public string WorkPath => _inner.WorkPath;

	/// <inheritdoc/>
	public bool IsSpoiled => _inner.IsSpoiled;

	/// <inheritdoc/>
	public async Task<ProcessResult> RunAsync(SandboxCommand command, CancellationToken cancellationToken)
	{
		ProcessResult result = await _inner.RunAsync(command, cancellationToken);
		if (command.Command == "git" && command.Arguments.Contains("bundle"))
		{
			await _tamper(_inner.OutputFolder);
		}

		return result;
	}

	/// <inheritdoc/>
	public Task WriteFileAsync(string path, string content, CancellationToken cancellationToken)
	{
		return _inner.WriteFileAsync(path, content, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<string?> ReadFileAsync(string path, int maxLength, CancellationToken cancellationToken)
	{
		return _inner.ReadFileAsync(path, maxLength, cancellationToken);
	}

	/// <inheritdoc/>
	public Task EndProcessesAsync(CancellationToken cancellationToken)
	{
		return _inner.EndProcessesAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
	{
		return _inner.DeleteFileAsync(path, cancellationToken);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
