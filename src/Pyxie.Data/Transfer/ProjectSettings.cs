using Pyxie.Data.Services.Inputs;

namespace Pyxie.Data.Transfer;

/// <summary>A project's settings as a settings file holds them, independent of the file's version and of any database: no ids and no positions, only what a person sets. A section is <see langword="null"/> when the file does not hold it, and an empty list when it holds none of its items, which an import takes to mean remove them all.</summary>
/// <param name="Agents">The agents, or <see langword="null"/>.</param>
/// <param name="Gates">The gates in the order they run, or <see langword="null"/>.</param>
/// <param name="Categories">The categories, or <see langword="null"/>.</param>
/// <param name="Sandbox">The sandbox settings, or <see langword="null"/>.</param>
public sealed record ProjectSettings(IReadOnlyList<AgentInput>? Agents, IReadOnlyList<GateInput>? Gates, IReadOnlyList<CategoryInput>? Categories, SandboxSettingsInput? Sandbox)
{
	/// <summary>Gets the sections the settings hold, in a fixed order: agents, gates, categories, sandbox.</summary>
	public IReadOnlyList<TransferSection> Sections => [.. Enum.GetValues<TransferSection>().Where(Has)];

	/// <summary>Determines whether the settings hold a section.</summary>
	/// <param name="section">The section.</param>
	/// <returns><see langword="true"/> when the section is there, even with no items.</returns>
	public bool Has(TransferSection section)
	{
		return section switch
		{
			TransferSection.Agents => Agents is not null,
			TransferSection.Gates => Gates is not null,
			TransferSection.Categories => Categories is not null,
			TransferSection.Sandbox => Sandbox is not null,
			_ => false,
		};
	}
}
