using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Pyxie.Runtime.Processes;

/// <summary>Runs commands to completion: git, gate commands, and agent harnesses. Output from both streams is captured as it arrives (and logged in full when asked), standard input is written and closed, and a process that times out or is cancelled is killed together with every process it started. On Windows every process also joins a job that ends with Pyxie, so nothing it started outlives it, even when Pyxie is killed.</summary>
public sealed class ProcessRunner : IDisposable
{
	// Once the process has exited (or been killed), the result waits at most this long for the rest of its output.
	private static readonly TimeSpan s_drainTimeout = TimeSpan.FromSeconds(2);

	private readonly TimeProvider _timeProvider;
	private readonly ChildProcessJob _job = new();

	/// <summary>Initializes a new instance of the <see cref="ProcessRunner"/> class.</summary>
	/// <param name="timeProvider">The clock that measures how long processes run.</param>
	public ProcessRunner(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	/// <summary>Ends every process this runner started that is still running.</summary>
	public void Dispose()
	{
		_job.Dispose();
	}

	/// <summary>Runs a command and waits for it to end.</summary>
	/// <param name="request">The command.</param>
	/// <param name="cancellationToken">A token that kills the process and its children when cancelled.</param>
	/// <returns>How the run ended and its output. Starting failures and timeouts are results, not exceptions.</returns>
	/// <exception cref="OperationCanceledException">The token was cancelled; the process tree has been killed.</exception>
	public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!Directory.Exists(request.WorkingDirectory))
		{
			return NotStarted($"The working directory {request.WorkingDirectory} does not exist.");
		}

		string? executable = CommandLine.Resolve(request.Command, request.WorkingDirectory);
		if (executable is null)
		{
			return NotStarted($"The command {request.Command} was not found.");
		}

		ProcessStartInfo startInfo = new()
		{
			WorkingDirectory = request.WorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
		};

		if (CommandLine.IsBatchFile(executable))
		{
			string? refusal = CommandLine.TryBuildBatchCommandLine(executable, request.Arguments, out string commandLine);
			if (refusal is not null)
			{
				return NotStarted(refusal);
			}

			startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
			startInfo.Arguments = commandLine;
		}
		else
		{
			startInfo.FileName = executable;
			foreach (string argument in request.Arguments)
			{
				startInfo.ArgumentList.Add(argument);
			}
		}

		foreach ((string name, string value) in request.Environment)
		{
			startInfo.Environment[name] = value;
		}

		(StreamWriter? log, string? logProblem) = TryOpenLog(request.LogPath);
		OutputCapture capture = new(request.MaxOutputLength, log, logProblem);
		OutputCapture standardOutput = new(request.MaxOutputLength, log: null, logProblem: null);
		using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
		TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
		process.Exited += (_, _) => exited.TrySetResult();
		process.OutputDataReceived += (_, line) =>
		{
			AddLine(capture, line);
			AddLine(standardOutput, line);
		};
		process.ErrorDataReceived += (_, line) => AddLine(capture, line);

		long started = _timeProvider.GetTimestamp();
		try
		{
			try
			{
				process.Start();
			}
			catch (Win32Exception exception)
			{
				return NotStarted($"The command {request.Command} could not start: {exception.Message}");
			}

			_job.Add(process);
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			using CancellationTokenSource timeout = new(request.Timeout, _timeProvider);
			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

			try
			{
				try
				{
					await WriteInputAsync(process, request.StandardInput, linked.Token);
				}
				catch (IOException)
				{
					// The process closed its input before reading all of it; its exit code says how it went.
				}

				// The process itself, not its output: a daemon it leaves behind (a build server, a watcher) can hold the output pipes open long after it exited.
				await exited.Task.WaitAsync(linked.Token);
			}
			catch (OperationCanceledException) when (linked.IsCancellationRequested)
			{
				await KillAsync(process);
				cancellationToken.ThrowIfCancellationRequested();

				return new ProcessResult(ProcessOutcome.TimedOut, -1, capture.ToString(), _timeProvider.GetElapsedTime(started), null) { StandardOutput = standardOutput.ToString() };
			}

			TimeSpan duration = _timeProvider.GetElapsedTime(started);
			await DrainAsync(process);

			return new ProcessResult(ProcessOutcome.Exited, process.ExitCode, capture.ToString(), duration, null) { StandardOutput = standardOutput.ToString() };
		}
		finally
		{
			capture.Seal();
			standardOutput.Seal();
			await CloseLogAsync(log);
		}
	}

	private static ProcessResult NotStarted(string error)
	{
		return new ProcessResult(ProcessOutcome.NotStarted, -1, string.Empty, TimeSpan.Zero, error);
	}

	// The log is best effort: a run whose log cannot be opened still runs, keeping its output in the result, and the excerpt says why there is no log.
	private static (StreamWriter? Log, string? Problem) TryOpenLog(string? path)
	{
		if (path is null || Path.GetDirectoryName(path) is not string folder)
		{
			return (null, null);
		}

		try
		{
			Directory.CreateDirectory(folder);

			return (new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true }, null);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return (null, $"The round log could not be opened: {exception.Message}");
		}
	}

	private static async Task CloseLogAsync(StreamWriter? log)
	{
		if (log is null)
		{
			return;
		}

		try
		{
			await log.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// The last lines may be missing from the log; the result keeps them.
		}
	}

	private static void AddLine(OutputCapture capture, DataReceivedEventArgs line)
	{
		// The final event of each stream carries null to mark its end.
		if (line.Data is not null)
		{
			capture.Add(line.Data);
		}
	}

	private static async Task WriteInputAsync(Process process, string? input, CancellationToken cancellationToken)
	{
		StreamWriter stdin = process.StandardInput;
		if (input is not null)
		{
			await stdin.WriteAsync(input.AsMemory(), cancellationToken);
			await stdin.FlushAsync(cancellationToken);
		}

		stdin.Close();
	}

	private static async Task KillAsync(Process process)
	{
		try
		{
			process.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException)
		{
			// It exited on its own between the timeout and the kill.
		}

		await DrainAsync(process);
	}

	// Gives the output that is still in the pipes a bounded time to arrive. A process left running by the one that exited (or that escaped a kill) can hold the pipes open; the excerpt keeps what arrived by then.
	private static async Task DrainAsync(Process process)
	{
		using CancellationTokenSource drain = new(s_drainTimeout);
		try
		{
			await process.WaitForExitAsync(drain.Token);
		}
		catch (OperationCanceledException) when (drain.IsCancellationRequested)
		{
		}
	}
}
