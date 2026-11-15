namespace Pyxie.Runtime.Orchestration;

/// <summary>How the pipeline runs. Bound from the <c>Pipeline</c> configuration section and checked at start-up.</summary>
public sealed class PipelineOptions
{
	/// <summary>The configuration section the options are read from.</summary>
	public const string SectionName = "Pipeline";

	/// <summary>Gets or sets how many cards run at once, from 1 to 32.</summary>
	public int Workers { get; set; } = 5;

	/// <summary>Gets or sets how often, in seconds, idle workers look for new work when nothing signals them, from 1 to 300.</summary>
	public int PollIntervalSeconds { get; set; } = 5;

	/// <summary>Gets or sets how long an agent may run in one round, in minutes, from 1 to 1440.</summary>
	public int RoundTimeoutMinutes { get; set; } = 60;
}
