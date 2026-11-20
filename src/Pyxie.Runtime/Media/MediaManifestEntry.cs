namespace Pyxie.Runtime.Media;

/// <summary>A media publication. Only path, name, and caption are accepted from an agent; provenance is assigned by Pyxie.</summary>
public sealed record MediaManifestEntry
{
	/// <summary>Gets the relative path beneath files/.</summary>
	public string Path { get; init; } = string.Empty;
	/// <summary>Gets the display filename.</summary>
	public string Name { get; init; } = string.Empty;
	/// <summary>Gets an optional plain-text caption.</summary>
	public string? Caption { get; init; }
	/// <summary>Gets the origin description on hand-in; ignored on hand-out.</summary>
	public string? Source { get; init; }
	/// <summary>Gets the actual byte count on hand-in; ignored on hand-out.</summary>
	public long? Length { get; init; }
	/// <summary>Gets the digest on hand-in; ignored on hand-out.</summary>
	public string? Sha256 { get; init; }
}
