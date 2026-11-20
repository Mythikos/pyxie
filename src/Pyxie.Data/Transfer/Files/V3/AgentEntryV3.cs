using System.Text.Json.Serialization;

using Pyxie.Data.Transfer.Files.V1;

namespace Pyxie.Data.Transfer.Files.V3;

/// <summary>A settings agent with its separately selected authentication method.</summary>
internal sealed record AgentEntryV3 : AgentEntryV1
{
	/// <summary>Gets the portable authentication method name.</summary>
	[JsonPropertyName("authentication")]
	public string? Authentication { get; init; }
}
