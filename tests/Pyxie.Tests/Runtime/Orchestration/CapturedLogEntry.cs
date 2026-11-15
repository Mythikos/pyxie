using Microsoft.Extensions.Logging;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>One message a test captured from the logger.</summary>
/// <param name="Level">The log level.</param>
/// <param name="Category">The logger category, which is the logging type's name.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception that was logged with it, if any.</param>
public sealed record CapturedLogEntry(LogLevel Level, string Category, string Message, Exception? Exception);
