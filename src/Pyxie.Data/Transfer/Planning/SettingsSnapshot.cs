using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Transfer.Planning;

/// <summary>A project's settings as stored, read untracked: what an export writes and what an import is planned against.</summary>
/// <param name="Agents">The agents in the order they were made.</param>
/// <param name="Gates">The gates in the order they run.</param>
/// <param name="Categories">The categories in the order they were made.</param>
/// <param name="Sandbox">The stored sandbox settings, or <see langword="null"/> while none were saved.</param>
internal sealed record SettingsSnapshot(IReadOnlyList<Agent> Agents, IReadOnlyList<Gate> Gates, IReadOnlyList<Category> Categories, SandboxSettings? Sandbox)
{
	/// <summary>Reads a project's settings.</summary>
	/// <param name="context">The context.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The settings as stored.</returns>
	public static async Task<SettingsSnapshot> ReadAsync(ProjectDbContext context, CancellationToken cancellationToken)
	{
		List<Agent> agents = await context.Agents.AsNoTracking().OrderBy(agent => agent.Id).ToListAsync(cancellationToken);
		List<Gate> gates = await context.Gates.AsNoTracking().OrderBy(gate => gate.SortOrder).ThenBy(gate => gate.Id).ToListAsync(cancellationToken);
		List<Category> categories = await context.Categories.AsNoTracking().OrderBy(category => category.Id).ToListAsync(cancellationToken);
		SandboxSettings? sandbox = await SandboxSettingsStore.FindAsync(context, cancellationToken);

		return new SettingsSnapshot(agents, gates, categories, sandbox);
	}

	/// <summary>Identifies the stored definitions in one section, including the IDs and order of its rows.</summary>
	public string RevisionOf(TransferSection section) => section switch
	{
		TransferSection.Agents => SettingsRevision.Hash(Agents),
		TransferSection.Gates => SettingsRevision.Hash(Gates),
		TransferSection.Categories => SettingsRevision.Hash(Categories),
		TransferSection.Sandbox => SettingsRevision.Hash(SandboxSettingsStore.EffectiveDockerfile(Sandbox)),
		_ => throw new ArgumentOutOfRangeException(nameof(section)),
	};

	/// <summary>Gets the settings as a settings file holds them: every section, agents and categories by name as the editors list them, gates in the order they run, and the Dockerfile in effect.</summary>
	/// <returns>The settings.</returns>
	public ProjectSettings ToSettings()
	{
		return new ProjectSettings(
			[.. Agents.OrderBy(agent => agent.Name, StringComparer.Ordinal).ThenBy(agent => agent.Id).Select(AgentInput.From)],
			[.. Gates.Select(GateInput.From)],
			[.. Categories.OrderBy(category => category.Name, StringComparer.Ordinal).ThenBy(category => category.Id).Select(CategoryInput.From)],
			new SandboxSettingsInput(SandboxSettingsStore.EffectiveDockerfile(Sandbox)));
	}
}
