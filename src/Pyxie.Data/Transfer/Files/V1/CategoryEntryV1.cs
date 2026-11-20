using System.Text.Json.Serialization;

namespace Pyxie.Data.Transfer.Files.V1;

/// <summary>A category in a version 1 settings file. Frozen: a change to it is a new version.</summary>
internal sealed record CategoryEntryV1
{
	/// <summary>Gets the name.</summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>Gets the color, written <c>#RRGGBB</c>.</summary>
	[JsonPropertyName("color")]
	public string? Color { get; init; }
}
