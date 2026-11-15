using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Runs the <c>docker</c> command line. A container engine that cannot be reached is reported as <see cref="SandboxUnavailableException"/>, whatever the command, so callers never mistake it for something wrong with a card.</summary>
internal sealed class DockerCli
{
	private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(2);

	// What the docker command line prints when there is no engine to talk to, on Windows (the named pipe) and elsewhere (the socket).
	private static readonly string[] s_engineDown =
	[
		"Cannot connect to the Docker daemon",
		"error during connect",
		"docker daemon is not running",
		"The system cannot find the file specified",
		"open //./pipe/",
	];

	private readonly ProcessRunner _runner;

	/// <summary>Initializes a new instance of the <see cref="DockerCli"/> class.</summary>
	/// <param name="runner">The process runner.</param>
	public DockerCli(ProcessRunner runner)
	{
		_runner = runner;
	}

	/// <summary>Gets the message for an engine that cannot be reached.</summary>
	public static string EngineDownMessage => "Docker is not running. Start Docker Desktop, then start the pipeline again.";

	/// <summary>Runs a docker command and returns how it ended.</summary>
	/// <param name="arguments">The arguments after <c>docker</c>.</param>
	/// <param name="cancellationToken">A token that stops the command.</param>
	/// <param name="timeout">How long it may run; two minutes when not given.</param>
	/// <param name="standardInput">Text for its standard input.</param>
	/// <param name="logPath">A file its full output is appended to.</param>
	/// <param name="maxOutputLength">How much output the result keeps.</param>
	/// <returns>The result.</returns>
	/// <exception cref="SandboxUnavailableException">docker could not start, or the engine does not answer.</exception>
	public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, TimeSpan? timeout = null, string? standardInput = null, string? logPath = null, int maxOutputLength = 1_000_000)
	{
		ProcessRequest request = new("docker", arguments, Path.GetTempPath())
		{
			Timeout = timeout ?? s_commandTimeout,
			StandardInput = standardInput,
			LogPath = logPath,
			MaxOutputLength = maxOutputLength,
		};

		ProcessResult result = await _runner.RunAsync(request, cancellationToken);
		if (result.Outcome == ProcessOutcome.NotStarted)
		{
			throw new SandboxUnavailableException($"The docker command could not start: {result.StartError} Install Docker Desktop, or put docker on the PATH.");
		}

		if (!result.Succeeded && IsEngineDown(result.Output))
		{
			throw new SandboxUnavailableException(EngineDownMessage);
		}

		return result;
	}

	/// <summary>Runs a docker command that must succeed and returns its standard output.</summary>
	/// <param name="arguments">The arguments after <c>docker</c>.</param>
	/// <param name="cancellationToken">A token that stops the command.</param>
	/// <param name="timeout">How long it may run; two minutes when not given.</param>
	/// <returns>What it printed on standard output.</returns>
	/// <exception cref="SandboxException">The command failed.</exception>
	/// <exception cref="SandboxUnavailableException">docker could not start, or the engine does not answer.</exception>
	public async Task<string> RunCheckedAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
	{
		ProcessResult result = await RunAsync(arguments, cancellationToken, timeout);

		return result.Succeeded ? result.StandardOutput : throw new SandboxException($"docker {arguments[0]} failed: {LastLine(result.Output)}");
	}

	/// <summary>Gets the last non-empty line of some output, which is where docker says what went wrong.</summary>
	/// <param name="output">The output.</param>
	/// <returns>The line, or a stand-in when there is none.</returns>
	public static string LastLine(string output)
	{
		string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		return lines.Length == 0 ? "no reason given." : lines[^1];
	}

	private static bool IsEngineDown(string output)
	{
		return s_engineDown.Any(text => output.Contains(text, StringComparison.OrdinalIgnoreCase));
	}
}
