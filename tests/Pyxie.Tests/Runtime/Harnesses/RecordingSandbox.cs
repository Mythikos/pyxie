using System.Collections.Concurrent;
using System.Globalization;

using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary>A sandbox that hands every call on to another and notes it first, so a test can see what the code under test asked of the sandbox and in what order: <c>write path</c>, <c>delete path</c>, <c>run command in folder</c>, <c>read path up to length</c>, and <c>end processes</c>. The commands it ran are kept whole. It never disposes the sandbox it wraps, which stays its owner's.</summary>
public sealed class RecordingSandbox : ISandbox
{
	private readonly ISandbox _inner;
	private readonly ConcurrentQueue<string> _calls = new();
	private readonly ConcurrentQueue<SandboxCommand> _commands = new();

	/// <summary>Initializes a new instance of the <see cref="RecordingSandbox"/> class.</summary>
	/// <param name="inner">The sandbox that does the work.</param>
	public RecordingSandbox(ISandbox inner)
	{
		_inner = inner;
	}

	/// <summary>Gets every call, in the order they were made.</summary>
	public IReadOnlyList<string> Calls => [.. _calls];

	/// <summary>Gets every command run, in order.</summary>
	public IReadOnlyList<SandboxCommand> Commands => [.. _commands];

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
	public Task<ProcessResult> RunAsync(SandboxCommand command, CancellationToken cancellationToken)
	{
		_calls.Enqueue($"run {command.Command} in {command.WorkingDirectory}");
		_commands.Enqueue(command);

		return _inner.RunAsync(command, cancellationToken);
	}

	/// <inheritdoc/>
	public Task WriteFileAsync(string path, string content, CancellationToken cancellationToken)
	{
		_calls.Enqueue($"write {path}");

		return _inner.WriteFileAsync(path, content, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<string?> ReadFileAsync(string path, int maxLength, CancellationToken cancellationToken)
	{
		_calls.Enqueue(string.Create(CultureInfo.InvariantCulture, $"read {path} up to {maxLength}"));

		return _inner.ReadFileAsync(path, maxLength, cancellationToken);
	}

	/// <inheritdoc/>
	public Task EndProcessesAsync(CancellationToken cancellationToken)
	{
		_calls.Enqueue("end processes");

		return _inner.EndProcessesAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
	{
		_calls.Enqueue($"delete {path}");

		return _inner.DeleteFileAsync(path, cancellationToken);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
