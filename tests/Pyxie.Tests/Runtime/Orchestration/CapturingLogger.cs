using Microsoft.Extensions.Logging;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Hands every message it receives to the <see cref="CapturingLoggerProvider"/> that created it.</summary>
public sealed class CapturingLogger : ILogger
{
	private readonly string _category;
	private readonly CapturingLoggerProvider _provider;

	/// <summary>Initializes a new instance of the <see cref="CapturingLogger"/> class.</summary>
	/// <param name="category">The logger category.</param>
	/// <param name="provider">The provider that keeps the messages.</param>
	public CapturingLogger(string category, CapturingLoggerProvider provider)
	{
		_category = category;
		_provider = provider;
	}

	/// <inheritdoc/>
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
	{
		return null;
	}

	/// <inheritdoc/>
	public bool IsEnabled(LogLevel logLevel)
	{
		return logLevel != LogLevel.None;
	}

	/// <inheritdoc/>
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		_provider.Add(new CapturedLogEntry(logLevel, _category, formatter(state, exception), exception));
	}
}
