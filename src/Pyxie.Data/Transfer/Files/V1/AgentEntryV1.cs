using System.Text.Json.Serialization;

namespace Pyxie.Data.Transfer.Files.V1;

/// <summary>An agent in a version 1 settings file. Frozen: a change to it is a new version.</summary>
internal record AgentEntryV1
{
	/// <summary>Gets the name.</summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>Gets the harness, by its <see cref="HarnessTokens"/> name.</summary>
	[JsonPropertyName("harness")]
	public string? Harness { get; init; }

	/// <summary>Gets the command.</summary>
	[JsonPropertyName("command")]
	public string? Command { get; init; }

	/// <summary>Gets the arguments, empty when there are none.</summary>
	[JsonPropertyName("arguments")]
	public string? Arguments { get; init; }

	/// <summary>Gets whether the agent can work on cards.</summary>
	[JsonPropertyName("canWork")]
	public bool? CanWork { get; init; }

	/// <summary>Gets whether the agent can review cards.</summary>
	[JsonPropertyName("canReview")]
	public bool? CanReview { get; init; }

	/// <summary>Gets whether the agent is the merge agent.</summary>
	[JsonPropertyName("canMerge")]
	public bool? CanMerge { get; init; }
}
