using System.Globalization;
using System.Text;

using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>A running Docker container made by <see cref="DockerSandboxes"/>. Each command runs with <c>docker exec</c> as the image's <c>node</c> user, through the image's <c>pyxie-exec</c>, which hands the sandbox's secrets to it as environment variables. Stopping a <c>docker exec</c> does not stop what it started inside, so a command that times out or is cancelled spoils the container, and disposing removes it.</summary>
internal sealed class DockerSandbox : ISandbox
{
	/// <summary>The input folder as commands in the container see it.</summary>
	public const string InputPathInside = "/pyxie/in";

	/// <summary>The output folder as commands in the container see it.</summary>
	public const string OutputPathInside = "/pyxie/out";

	/// <summary>The container's working folder.</summary>
	public const string WorkPathInside = "/work";

	private const string User = "node";
	private const string Exec = "/usr/local/bin/pyxie-exec";

	private readonly DockerCli _docker;
	private readonly SandboxSpec _spec;
	private bool _spoiled;
	private bool _disposed;

	/// <summary>Initializes a new instance of the <see cref="DockerSandbox"/> class.</summary>
	/// <param name="docker">The docker command line.</param>
	/// <param name="spec">What the container was made with.</param>
	public DockerSandbox(DockerCli docker, SandboxSpec spec)
	{
		_docker = docker;
		_spec = spec;
	}

	/// <inheritdoc/>
	public string Name => _spec.Name;

	/// <inheritdoc/>
	public string InputFolder => _spec.InputFolder;

	/// <inheritdoc/>
	public string OutputFolder => _spec.OutputFolder;

	/// <inheritdoc/>
	public string InputPath => InputPathInside;

	/// <inheritdoc/>
	public string OutputPath => OutputPathInside;

	/// <inheritdoc/>
	public string WorkPath => WorkPathInside;

	/// <inheritdoc/>
	public bool IsSpoiled => _spoiled;

	/// <inheritdoc/>
	public async Task<ProcessResult> RunAsync(SandboxCommand command, CancellationToken cancellationToken)
	{
		List<string> arguments = ["exec"];
		if (command.StandardInput is not null)
		{
			arguments.Add("--interactive");
		}

		// One argument each, so a value with spaces, quotes, or an equals sign reaches the command as written. A name with an equals sign would be read as part of its value.
		foreach ((string name, string value) in command.Environment)
		{
			if (name.Length == 0 || name.Contains('=', StringComparison.Ordinal))
			{
				throw new ArgumentException($"'{name}' is not an environment variable name.", nameof(command));
			}

			arguments.AddRange(["--env", $"{name}={value}"]);
		}

		arguments.AddRange(["--user", User, "--workdir", command.WorkingDirectory, Name, Exec, command.Command, .. command.Arguments]);

		ProcessResult result;
		try
		{
			result = await _docker.RunAsync(arguments, cancellationToken, command.Timeout, command.StandardInput, command.LogPath, command.MaxOutputLength);
		}
		catch (OperationCanceledException)
		{
			_spoiled = true;

			throw;
		}

		if (result.Outcome == ProcessOutcome.TimedOut)
		{
			_spoiled = true;

			return result;
		}

		if (result.Outcome == ProcessOutcome.Exited && result.ExitCode is 125 or 126 or 127)
		{
			return Classify(result);
		}

		return result;
	}

	/// <inheritdoc/>
	public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken)
	{
		ProcessResult result = await _docker.RunAsync(["exec", "--interactive", "--user", User, Name, "sh", "-c", "mkdir -p \"$(dirname \"$1\")\" && cat > \"$1\"", "sh", path], cancellationToken, standardInput: content);

		if (!result.Succeeded)
		{
			throw new SandboxException($"Pyxie could not write {path} in the sandbox: {DockerCli.LastLine(result.Output)}");
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The file is copied, at most <paramref name="maxLength"/> bytes of it, into the output folder and read there, byte for byte; a command's output is captured line by line and cut to fit, which would change the text. A copy that is not a plain file is treated as no file.
	/// </remarks>
	public async Task<string?> ReadFileAsync(string path, int maxLength, CancellationToken cancellationToken)
	{
		string copy = $".pyxie-read-{Guid.NewGuid():N}";
		string hostCopy = Path.Combine(OutputFolder, copy);

		try
		{
			ProcessResult result = await _docker.RunAsync(["exec", "--user", User, Name, "sh", "-c", "test -f \"$1\" && head -c \"$2\" \"$1\" > \"$3\"", "sh", path, maxLength.ToString(CultureInfo.InvariantCulture), $"{OutputPathInside}/{copy}"], cancellationToken);

			FileInfo file = new(hostCopy);
			if (!result.Succeeded || !file.Exists || file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
			{
				return null;
			}

			byte[] bytes = await File.ReadAllBytesAsync(hostCopy, cancellationToken);

			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes, 0, Math.Min(bytes.Length, maxLength));
		}
		finally
		{
			File.Delete(hostCopy);
		}
	}

	/// <inheritdoc/>
	public async Task EndProcessesAsync(CancellationToken cancellationToken)
	{
		ProcessResult result = await _docker.RunAsync(["exec", "--user", "root", Name, "pkill", "-KILL", "-u", User], cancellationToken);
		if (result.Outcome != ProcessOutcome.Exited || result.ExitCode is not (0 or 1))
		{
			string running = await _docker.RunCheckedAsync(["inspect", "--format", "{{.State.Running}}", Name], cancellationToken);
			if (running.Trim() != "false")
			{
				throw new SandboxException("The sandbox could not stop its processes before collecting output.");
			}
		}
	}

	/// <inheritdoc/>
	public async Task DeleteFileAsync(string path, CancellationToken cancellationToken)
	{
		await _docker.RunAsync(["exec", "--user", User, Name, "rm", "-f", path], cancellationToken);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (_spec.Keep && !_spoiled)
		{
			return;
		}

		try
		{
			await _docker.RunAsync(["rm", "--force", Name], CancellationToken.None);
		}
		catch (SandboxUnavailableException)
		{
			// The engine is gone, and the container with it or with the next cleanup.
		}
	}

	// Exit codes 125 to 127 come from docker or the container runtime when the command never ran: a missing or non-executable program is a command that could not start, and a container that is gone is a sandbox that broke.
	private ProcessResult Classify(ProcessResult result)
	{
		string output = result.Output;
		if (output.Contains("No such container", StringComparison.OrdinalIgnoreCase) || output.Contains("is not running", StringComparison.OrdinalIgnoreCase))
		{
			_spoiled = true;

			throw new SandboxException("The sandbox stopped while the round was running.");
		}

		if ((output.Contains("pyxie-exec: ", StringComparison.Ordinal) && output.Contains(": command not found", StringComparison.Ordinal)) || output.Contains("executable file not found", StringComparison.OrdinalIgnoreCase) || output.Contains("OCI runtime exec failed", StringComparison.OrdinalIgnoreCase) || (output.Contains("permission denied", StringComparison.OrdinalIgnoreCase) && result.ExitCode == 126))
		{
			return result with { Outcome = ProcessOutcome.NotStarted, StartError = DockerCli.LastLine(output) };
		}

		return result;
	}
}
