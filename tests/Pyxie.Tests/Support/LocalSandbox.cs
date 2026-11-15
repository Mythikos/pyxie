using System.Text;

using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Support;

/// <summary>A stand-in for a sandbox in tests: commands run as ordinary processes in a folder of their own, with no isolation, so the runtime's rounds, bundles, and merges can be tested without a container engine. A worker's folder outlives the sandbox the way a container's volume does; any other sandbox's folder goes when it is disposed.</summary>
public sealed class LocalSandbox : ISandbox
{
	private readonly ProcessRunner _runner;
	private readonly SandboxSpec _spec;
	private readonly string _workFolder;
	private bool _spoiled;

	/// <summary>Initializes a new instance of the <see cref="LocalSandbox"/> class.</summary>
	/// <param name="runner">The process runner commands run through.</param>
	/// <param name="spec">What the sandbox was made with.</param>
	/// <param name="workFolder">The folder that stands for the sandbox's working folder.</param>
	public LocalSandbox(ProcessRunner runner, SandboxSpec spec, string workFolder)
	{
		_runner = runner;
		_spec = spec;
		_workFolder = workFolder;
	}

	/// <inheritdoc/>
	public string Name => _spec.Name;

	/// <inheritdoc/>
	public string InputFolder => _spec.InputFolder;

	/// <inheritdoc/>
	public string OutputFolder => _spec.OutputFolder;

	/// <inheritdoc/>
	public string InputPath => Slashed(_spec.InputFolder);

	/// <inheritdoc/>
	public string OutputPath => Slashed(_spec.OutputFolder);

	/// <inheritdoc/>
	public string WorkPath => Slashed(_workFolder);

	/// <inheritdoc/>
	public bool IsSpoiled => _spoiled;

	/// <summary>Gets the spec the sandbox was started with, so a test can see what it was given.</summary>
	public SandboxSpec Spec => _spec;

	/// <inheritdoc/>
	public async Task<ProcessResult> RunAsync(SandboxCommand command, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(command.WorkingDirectory);
		ProcessRequest request = new(command.Command, command.Arguments, command.WorkingDirectory)
		{
			StandardInput = command.StandardInput,
			Timeout = command.Timeout,
			LogPath = command.LogPath,
			MaxOutputLength = command.MaxOutputLength,
			Environment = command.Environment,
		};

		try
		{
			ProcessResult result = await _runner.RunAsync(request, cancellationToken);
			if (result.Outcome == ProcessOutcome.TimedOut)
			{
				_spoiled = true;
			}

			return result;
		}
		catch (OperationCanceledException)
		{
			_spoiled = true;

			throw;
		}
	}

	/// <inheritdoc/>
	public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? _workFolder);
		await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<string?> ReadFileAsync(string path, int maxLength, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		string text = await File.ReadAllTextAsync(path, cancellationToken);

		return text.Length > maxLength ? text[..maxLength] : text;
	}

	/// <inheritdoc/>
	public Task EndProcessesAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
	{
		// Like rm -f: a file that is not there, also in a folder that is not there, is already deleted.
		if (File.Exists(path))
		{
			File.Delete(path);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		if (!_spec.Keep)
		{
			LocalSandboxes.DeleteFolder(_workFolder);
		}

		return ValueTask.CompletedTask;
	}

	private static string Slashed(string path)
	{
		return Path.GetFullPath(path).Replace('\\', '/');
	}
}
