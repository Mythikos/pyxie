using System.Text.Json.Serialization;

namespace Pyxie.Data.Transfer.Files.V1;

/// <summary>A version 1 settings file as it is written and read. Frozen: a change to it is a new version. Every member is nullable so a missing one is reported by name instead of becoming a default; the members' JSON names are their own and never follow the C# names.</summary>
internal sealed record SettingsDocumentV1
{
	/// <summary>Gets the format marker, <see cref="SettingsFileFormat.Name"/>.</summary>
	[JsonPropertyName("format")]
	public string? Format { get; init; }

	/// <summary>Gets the file version, 1.</summary>
	[JsonPropertyName("version")]
	public int? Version { get; init; }

	/// <summary>Gets the agents, or <see langword="null"/> when the file does not hold them.</summary>
	[JsonPropertyName("agents")]
	public List<AgentEntryV1?>? Agents { get; init; }

	/// <summary>Gets the gates in the order they run, or <see langword="null"/> when the file does not hold them.</summary>
	[JsonPropertyName("gates")]
	public List<GateEntryV1?>? Gates { get; init; }

	/// <summary>Gets the categories, or <see langword="null"/> when the file does not hold them.</summary>
	[JsonPropertyName("categories")]
	public List<CategoryEntryV1?>? Categories { get; init; }

	/// <summary>Gets the sandbox settings, or <see langword="null"/> when the file does not hold them.</summary>
	[JsonPropertyName("sandbox")]
	public SandboxEntryV1? Sandbox { get; init; }
}
