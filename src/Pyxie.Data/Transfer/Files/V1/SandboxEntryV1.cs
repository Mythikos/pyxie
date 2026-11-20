using System.Text.Json.Serialization;

namespace Pyxie.Data.Transfer.Files.V1;

/// <summary>The sandbox settings in a version 1 settings file. Frozen: a change to it is a new version.</summary>
internal sealed record SandboxEntryV1
{
	/// <summary>Gets the project image's Dockerfile.</summary>
	[JsonPropertyName("dockerfile")]
	public string? Dockerfile { get; init; }
}
