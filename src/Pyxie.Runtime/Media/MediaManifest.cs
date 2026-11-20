namespace Pyxie.Runtime.Media;

/// <summary>Versioned media exchange, identical in shape on hand-in and hand-out. Paths are relative to its media folder.</summary>
public sealed record MediaManifest
{
	/// <summary>Gets the exchange version.</summary>
	public int Version { get; init; } = 1;
	/// <summary>Gets the explicitly published files.</summary>
	public IReadOnlyList<MediaManifestEntry> Files { get; init; } = [];
}
