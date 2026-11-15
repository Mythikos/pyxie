namespace Pyxie.Data.Entities;

/// <summary>An automated check the gate rounds run in each card's workspace, such as a build, the tests, or a linter.</summary>
public class Gate
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the unique, case-insensitive display name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the executable to run.</summary>
	public string Command { get; set; } = string.Empty;

	/// <summary>Gets or sets the command-line arguments, empty when there are none.</summary>
	public string Arguments { get; set; } = string.Empty;

	/// <summary>Gets or sets how long the gate may run before it is stopped and counted as failed.</summary>
	public int TimeoutMinutes { get; set; } = Limits.PyxieLimits.DefaultGateTimeoutMinutes;

	/// <summary>Gets or sets the zero-based position in the order gates run.</summary>
	public int SortOrder { get; set; }

	/// <summary>Gets or sets a value indicating whether gate rounds run this gate.</summary>
	public bool IsEnabled { get; set; } = true;
}
