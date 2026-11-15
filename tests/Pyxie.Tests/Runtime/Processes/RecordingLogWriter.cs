using System.Text;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>A log writer for <see cref="Pyxie.Runtime.Processes.OutputCapture"/> tests that records what it was given and can fail on a chosen write, as a full disk or a locked file would.</summary>
public sealed class RecordingLogWriter : TextWriter
{
	private readonly bool _keepLines;
	private readonly int? _failOnWrite;
	private readonly Func<Exception>? _failure;

	/// <summary>Initializes a new instance of the <see cref="RecordingLogWriter"/> class.</summary>
	/// <param name="failOnWrite">The 1-based write that throws; <see langword="null"/> for a writer that never fails.</param>
	/// <param name="failure">Creates the exception a failing write throws.</param>
	/// <param name="keepLines">Whether to keep every line, which a test that writes tens of megabytes does not want.</param>
	public RecordingLogWriter(int? failOnWrite = null, Func<Exception>? failure = null, bool keepLines = true)
	{
		_failOnWrite = failOnWrite;
		_failure = failure;
		_keepLines = keepLines;
	}

	/// <summary>Gets how many times the capture tried to write a line, failed attempts included.</summary>
	public int Attempts { get; private set; }

	/// <summary>Gets the lines that were written without failing, when lines are kept.</summary>
	public List<string> Lines { get; } = [];

	/// <summary>Gets how many lines were written without failing.</summary>
	public int WrittenLines { get; private set; }

	/// <summary>Gets how many characters were written without failing, line breaks not counted.</summary>
	public long Characters { get; private set; }

	/// <summary>Gets the last line that was written without failing.</summary>
	public string? LastLine { get; private set; }

	/// <inheritdoc/>
	public override Encoding Encoding => Encoding.UTF8;

	/// <inheritdoc/>
	public override void WriteLine(string? value)
	{
		Attempts++;
		if (Attempts == _failOnWrite)
		{
			throw _failure?.Invoke() ?? new IOException("There is not enough space on the disk.");
		}

		string line = value ?? string.Empty;
		WrittenLines++;
		Characters += line.Length;
		LastLine = line;
		if (_keepLines)
		{
			Lines.Add(line);
		}
	}
}
