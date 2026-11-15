using System.Globalization;
using System.Text;

namespace Pyxie.Runtime.Processes;

/// <summary>Collects a process's output lines from both streams as they arrive, in bounded memory: the beginning is kept whole and the end in a rolling window, so the excerpt keeps what usually matters, the first error and the final summary, whatever the output's size. Every line also goes to the log, when there is one, up to a size limit. The log is best effort: a log that could not be opened, or a write that fails (a full disk, a locked file), is noted at the start of the excerpt, and a failed write stops the logging, because an exception here would surface on a stream reader thread and end the whole process. Safe to call from the stream reader threads.</summary>
internal sealed class OutputCapture
{
	// A log is for reading what a round did; a process that prints without end must not fill the disk.
	private const long MaxLogLength = 50_000_000;

	private readonly Lock _gate = new();
	private readonly int _headLength;
	private readonly int _tailLength;
	private readonly StringBuilder _head = new();
	private readonly LinkedList<string> _tail = new();
	private TextWriter? _log;
	private long _logged;
	private string? _logProblem;
	private int _tailSize;
	private long _omitted;
	private bool _sealed;

	/// <summary>Initializes a new instance of the <see cref="OutputCapture"/> class.</summary>
	/// <param name="maxLength">The most characters the excerpt keeps, not counting the marker for what was cut.</param>
	/// <param name="log">A writer that receives every line in full, or <see langword="null"/>.</param>
	/// <param name="logProblem">Why there is no log although one was asked for, noted at the start of the excerpt; otherwise <see langword="null"/>.</param>
	public OutputCapture(int maxLength, TextWriter? log, string? logProblem)
	{
		_headLength = maxLength / 3;
		_tailLength = maxLength - _headLength;
		_log = log;
		_logProblem = logProblem;
	}

	/// <summary>Adds one line of output.</summary>
	/// <param name="line">The line, without its line break.</param>
	public void Add(string line)
	{
		lock (_gate)
		{
			if (_sealed)
			{
				return;
			}

			Log(line);

			string text = line + "\n";
			if (_tail.Count == 0 && _head.Length < _headLength)
			{
				int taken = Math.Min(_headLength - _head.Length, text.Length);
				_head.Append(text, 0, taken);
				text = text[taken..];
				if (text.Length == 0)
				{
					return;
				}
			}

			_tail.AddLast(text);
			_tailSize += text.Length;

			while (_tailSize > _tailLength && _tail.First is LinkedListNode<string> first)
			{
				int excess = _tailSize - _tailLength;
				if (first.Value.Length <= excess)
				{
					_tail.RemoveFirst();
					_tailSize -= first.Value.Length;
					_omitted += first.Value.Length;
				}
				else
				{
					first.Value = first.Value[excess..];
					_tailSize -= excess;
					_omitted += excess;
				}
			}
		}
	}

	private void Log(string line)
	{
		if (_log is null)
		{
			return;
		}

		try
		{
			if (_logged + line.Length > MaxLogLength)
			{
				_log.WriteLine("[The log stops here: it reached its size limit.]");
				_log = null;
				return;
			}

			_log.WriteLine(line);
			_logged += line.Length + 1;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
		{
			_log = null;
			_logProblem = $"The round log could not be written: {exception.Message}";
		}
	}

	/// <summary>Stops taking lines, so output that arrives after the run ended (from a process tree that is still winding down) never reaches a closed log.</summary>
	public void Seal()
	{
		lock (_gate)
		{
			_sealed = true;
		}
	}

	/// <summary>Gets the excerpt: everything when it fit, otherwise the beginning, a marker saying how much was cut, and the end.</summary>
	/// <returns>The captured output without a trailing line break.</returns>
	public override string ToString()
	{
		lock (_gate)
		{
			StringBuilder text = new(_head.ToString());
			if (_logProblem is not null)
			{
				text.Insert(0, $"[{_logProblem}]\n");
			}

			if (_omitted > 0)
			{
				text.Append(CultureInfo.InvariantCulture, $"[truncated: {_omitted} characters omitted]\n");
			}

			foreach (string part in _tail)
			{
				text.Append(part);
			}

			return text.ToString().TrimEnd('\n');
		}
	}
}
