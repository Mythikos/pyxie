using System.Text.Json.Serialization;

namespace Pyxie.Data.Transfer.Files.V1;

/// <summary>A gate in a version 1 settings file, whose place in its list is its place in the order gates run. Frozen: a change to it is a new version.</summary>
internal sealed record GateEntryV1
{
	/// <summary>Gets the name.</summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>Gets the command.</summary>
	[JsonPropertyName("command")]
	public string? Command { get; init; }

	/// <summary>Gets the arguments, empty when there are none.</summary>
	[JsonPropertyName("arguments")]
	public string? Arguments { get; init; }

	/// <summary>Gets the timeout in minutes.</summary>
	[JsonPropertyName("timeoutMinutes")]
	public int? TimeoutMinutes { get; init; }

	/// <summary>Gets whether gate rounds run the gate.</summary>
	[JsonPropertyName("enabled")]
	public bool? Enabled { get; init; }
}
