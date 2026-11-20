using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Transfer.Planning;

/// <summary>How importing the picked sections of a settings file would change a project as it is stored now. The preview, the import, and the import's check after it has written all come from the same plan, so what a person reviews is what is written.</summary>
/// <param name="Snapshot">The project's settings the plan was made against.</param>
/// <param name="Agents">How the file's agents meet the project's, or <see langword="null"/> when agents are not imported.</param>
/// <param name="Gates">How the file's gates meet the project's, or <see langword="null"/> when gates are not imported.</param>
/// <param name="Categories">How the file's categories meet the project's, or <see langword="null"/> when categories are not imported.</param>
/// <param name="Dockerfile">The file's Dockerfile, normalized, or <see langword="null"/> when the sandbox settings are not imported.</param>
internal sealed record SettingsImportPlan(SettingsSnapshot Snapshot, NamePlan<Agent, AgentInput>? Agents, NamePlan<Gate, GateInput>? Gates, NamePlan<Category, CategoryInput>? Categories, string? Dockerfile)
{
	/// <summary>Gets a value indicating whether the sandbox Dockerfile changes.</summary>
	public bool DockerfileChanges => Dockerfile is string dockerfile && !string.Equals(dockerfile, SandboxSettingsStore.EffectiveDockerfile(Snapshot.Sandbox), StringComparison.Ordinal);

	/// <summary>Gets what the import changes in a section.</summary>
	/// <param name="section">A section the plan covers.</param>
	/// <returns>The section's changes; none for a section the plan does not cover.</returns>
	public SectionChanges ChangesOf(TransferSection section)
	{
		return section switch
		{
			TransferSection.Agents when Agents is NamePlan<Agent, AgentInput> agents => Changes(section, agents, agent => agent.Name, input => input.Name, (agent, input) => AgentInput.From(agent) != input, reordered: false),
			TransferSection.Gates when Gates is NamePlan<Gate, GateInput> gates => Changes(section, gates, gate => gate.Name, input => input.Name, (gate, input) => GateInput.From(gate) != input, IsReordered(gates)),
			TransferSection.Categories when Categories is NamePlan<Category, CategoryInput> categories => Changes(section, categories, category => category.Name, input => input.Name, (category, input) => CategoryInput.From(category) != input, reordered: false),
			TransferSection.Sandbox when Dockerfile is not null => new SectionChanges(section, [], DockerfileChanges ? ["Dockerfile"] : [], [], false),
			_ => SectionChanges.None(section),
		};
	}

	private static SectionChanges Changes<TExisting, TIncoming>(TransferSection section, NamePlan<TExisting, TIncoming> plan, Func<TExisting, string> existingName, Func<TIncoming, string> incomingName, Func<TExisting, TIncoming, bool> differs, bool reordered)
	{
		return new SectionChanges(section, [.. plan.Added.Select(incomingName)], [.. plan.Matched.Where(pair => differs(pair.Existing, pair.Incoming)).Select(pair => incomingName(pair.Incoming))], [.. plan.Removed.Select(existingName)], reordered);
	}

	// The gates the project keeps change their order when the file lists them in another order than they run in now.
	private bool IsReordered(NamePlan<Gate, GateInput> gates)
	{
		HashSet<int> kept = [.. gates.Matched.Select(pair => pair.Existing.Id)];

		return !Snapshot.Gates.Where(gate => kept.Contains(gate.Id)).Select(gate => gate.Id).SequenceEqual(gates.Matched.Select(pair => pair.Existing.Id));
	}
}
