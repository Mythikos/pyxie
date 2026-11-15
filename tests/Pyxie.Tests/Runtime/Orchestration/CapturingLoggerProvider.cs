using Microsoft.Extensions.Logging;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Keeps every message logged through it, so a test can check what the runtime reported about a failure it survived.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
	private readonly Lock _gate = new();
	private readonly List<CapturedLogEntry> _entries = [];

	/// <summary>Gets a snapshot of the messages logged so far, oldest first.</summary>
	public IReadOnlyList<CapturedLogEntry> Entries
	{
		get
		{
			lock (_gate)
			{
				return [.. _entries];
			}
		}
	}

	/// <inheritdoc/>
	public ILogger CreateLogger(string categoryName)
	{
		return new CapturingLogger(categoryName, this);
	}

	/// <summary>Counts the messages of a level that contain a text.</summary>
	/// <param name="level">The level.</param>
	/// <param name="text">Text the message contains.</param>
	/// <returns>How many messages matched.</returns>
	public int Count(LogLevel level, string text)
	{
		return Entries.Count(entry => entry.Level == level && entry.Message.Contains(text, StringComparison.Ordinal));
	}

	/// <summary>Records a message. Called by the loggers this provider created.</summary>
	/// <param name="entry">The message.</param>
	public void Add(CapturedLogEntry entry)
	{
		lock (_gate)
		{
			_entries.Add(entry);
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
	}
}
