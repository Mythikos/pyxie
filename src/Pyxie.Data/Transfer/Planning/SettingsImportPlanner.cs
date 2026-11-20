namespace Pyxie.Data.Transfer.Planning;

/// <summary>Plans an import of the picked sections of a settings file against a project's stored settings, matching items by name (<see cref="NameMatcher"/>).</summary>
internal static class SettingsImportPlanner
{
	/// <summary>Plans an import.</summary>
	/// <param name="snapshot">The project's settings as stored.</param>
	/// <param name="settings">The file's settings, normalized by <see cref="SettingsRules.Normalize"/>, holding every picked section with no rule problems.</param>
	/// <param name="sections">The sections to import.</param>
	/// <returns>The plan.</returns>
	public static SettingsImportPlan Plan(SettingsSnapshot snapshot, ProjectSettings settings, IReadOnlySet<TransferSection> sections)
	{
		return new SettingsImportPlan(
			snapshot,
			sections.Contains(TransferSection.Agents) && settings.Agents is not null ? NameMatcher.Match(snapshot.Agents, agent => agent.Name, settings.Agents, input => input.Name) : null,
			sections.Contains(TransferSection.Gates) && settings.Gates is not null ? NameMatcher.Match(snapshot.Gates, gate => gate.Name, settings.Gates, input => input.Name) : null,
			sections.Contains(TransferSection.Categories) && settings.Categories is not null ? NameMatcher.Match(snapshot.Categories, category => category.Name, settings.Categories, input => input.Name) : null,
			sections.Contains(TransferSection.Sandbox) ? settings.Sandbox?.Dockerfile : null);
	}
}
