using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Transfer.Planning;

/// <summary>Applies the editors' rules to the settings a file holds, section by section, before anything is planned or written: each item's fields through the same rules the services use, names unique ignoring case, and at most one merge agent. A section that breaks a rule cannot be imported, while the other sections still can, so a rule that tightens in a later Pyxie blocks only the sections it concerns.</summary>
internal static class SettingsRules
{
	/// <summary>Gets settings as they are checked and stored: names, commands, and arguments trimmed, and the Dockerfile normalized.</summary>
	/// <param name="settings">The settings as the file holds them.</param>
	/// <returns>The normalized settings.</returns>
	public static ProjectSettings Normalize(ProjectSettings settings)
	{
		return new ProjectSettings(
			settings.Agents?.Select(AgentInputRules.Normalize).ToList(),
			settings.Gates?.Select(GateInputRules.Normalize).ToList(),
			settings.Categories?.Select(CategoryInputRules.Normalize).ToList(),
			settings.Sandbox is SandboxSettingsInput sandbox ? new SandboxSettingsInput(SandboxSettingsInputRules.NormalizeDockerfile(sandbox.Dockerfile)) : null);
	}

	/// <summary>Lists what stops a section of normalized settings from being imported.</summary>
	/// <param name="settings">The settings, normalized by <see cref="Normalize"/>.</param>
	/// <param name="section">The section, which the settings hold.</param>
	/// <returns>Every problem, each naming its item; empty when the section can be imported.</returns>
	public static IReadOnlyList<string> Check(ProjectSettings settings, TransferSection section)
	{
		List<string> problems = [];
		switch (section)
		{
			case TransferSection.Agents when settings.Agents is IReadOnlyList<AgentInput> agents:
				CheckItems(agents, "Agent", agent => agent.Name, AgentInputRules.Validate, problems);
				CheckUnique(agents, "agent", agent => agent.Name, problems);
				List<string> mergeAgents = [.. agents.Where(agent => agent.CanMerge).Select(agent => $"'{agent.Name}'")];
				if (mergeAgents.Count > 1)
				{
					problems.Add($"The file makes {EnglishLists.JoinWithAnd(mergeAgents)} merge agents. Only one agent can resolve merge conflicts.");
				}

				break;

			case TransferSection.Gates when settings.Gates is IReadOnlyList<GateInput> gates:
				CheckItems(gates, "Gate", gate => gate.Name, GateInputRules.Validate, problems);
				CheckUnique(gates, "gate", gate => gate.Name, problems);
				break;

			case TransferSection.Categories when settings.Categories is IReadOnlyList<CategoryInput> categories:
				CheckItems(categories, "Category", category => category.Name, category => CategoryInputRules.ValidateName(category.Name), problems);
				CheckUnique(categories, "category", category => category.Name, problems);
				break;

			case TransferSection.Sandbox when settings.Sandbox is SandboxSettingsInput sandbox:
				if (SandboxSettingsInputRules.ValidateDockerfile(sandbox.Dockerfile) is string dockerfileError)
				{
					problems.Add(dockerfileError);
				}

				break;
		}

		return problems;
	}

	private static void CheckItems<TItem>(IReadOnlyList<TItem> items, string kind, Func<TItem, string> nameOf, Func<TItem, string?> validate, List<string> problems)
	{
		for (int index = 0; index < items.Count; index++)
		{
			if (validate(items[index]) is string error)
			{
				string name = nameOf(items[index]);
				problems.Add($"{(name.Length == 0 ? $"{kind} {index + 1}" : $"{kind} '{name}'")}: {error}");
			}
		}
	}

	private static void CheckUnique<TItem>(IReadOnlyList<TItem> items, string kind, Func<TItem, string> nameOf, List<string> problems)
	{
		foreach (IGrouping<string, TItem> group in items.Where(item => nameOf(item).Length > 0).GroupBy(nameOf, InputRules.NameComparer).Where(group => group.Count() > 1))
		{
			problems.Add($"The file has more than one {kind} named '{group.Key}'.");
		}
	}
}
