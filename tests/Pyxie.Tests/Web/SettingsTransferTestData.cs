using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The settings files and project rows the tests of the Export and Import controls and the import dialog arrange, and what they read back: a project starts with the seeded categories Feature, Bug, and Chore, and no agents or gates.</summary>
internal static class SettingsTransferTestData
{
	/// <summary>The seeded categories as a settings file holds them, without Chore, so importing them removes Chore.</summary>
	public static readonly IReadOnlyList<CategoryInput> SeededCategoriesWithoutChore = [new CategoryInput("Feature", CategoryColors.Green), new CategoryInput("Bug", CategoryColors.Pink)];

	/// <summary>The seeded categories as a settings file holds them, with Docs, so importing them adds Docs and changes nothing else.</summary>
	public static readonly IReadOnlyList<CategoryInput> SeededCategoriesWithDocs = [.. SeededCategoriesWithoutChore, new CategoryInput("Chore", CategoryColors.Neutral), new CategoryInput("Docs", CategoryColors.Neutral)];

	/// <summary>A Dockerfile that differs from the default one, so importing it changes the sandbox settings.</summary>
	public const string ImportedDockerfile = "FROM ${PYXIE_BASE}\nRUN echo imported\n";

	/// <summary>Gets an agent as a settings file holds it.</summary>
	/// <param name="name">The agent's name.</param>
	/// <param name="canWork">Whether it can work; yes unless given.</param>
	/// <param name="canReview">Whether it can review; yes unless given.</param>
	/// <returns>The agent's fields.</returns>
	public static AgentInput Agent(string name, bool canWork = true, bool canReview = true)
	{
		return new AgentInput(name, HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview);
	}

	/// <summary>Gets a gate as a settings file holds it.</summary>
	/// <param name="name">The gate's name.</param>
	/// <returns>The gate's fields.</returns>
	public static GateInput Gate(string name)
	{
		return new GateInput(name, "dotnet", "build", 10, IsEnabled: true);
	}

	/// <summary>Gets a settings file that holds one section, which adds something to the stand-in project: the agent Claude, the gate Build, the category Docs, or a Dockerfile of its own.</summary>
	/// <param name="section">The section the file holds.</param>
	/// <returns>The file's settings.</returns>
	public static ProjectSettings FileAdding(TransferSection section)
	{
		return section switch
		{
			TransferSection.Agents => new ProjectSettings(Agents: [Agent("Claude")], Gates: null, Categories: null, Sandbox: null),
			TransferSection.Gates => new ProjectSettings(Agents: null, Gates: [Gate("Build")], Categories: null, Sandbox: null),
			TransferSection.Categories => new ProjectSettings(Agents: null, Gates: null, Categories: SeededCategoriesWithDocs, Sandbox: null),
			_ => new ProjectSettings(Agents: null, Gates: null, Categories: null, Sandbox: new SandboxSettingsInput(ImportedDockerfile)),
		};
	}

	/// <summary>Adds a category to the project directly, as another tab or the category panel would while a file is reviewed.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="name">The category's name.</param>
	/// <returns>A task that completes when the category is stored.</returns>
	public static async Task AddCategoryAsync(TestDatabase database, string name)
	{
		await using ProjectDbContext context = database.CreateContext();
		context.Categories.Add(new Category { Name = name, Color = CategoryColors.Neutral });
		await context.SaveChangesAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads the names of the project's agents.</summary>
	/// <param name="database">The database to read.</param>
	/// <returns>The names, in the order the agent service lists them.</returns>
	public static async Task<IReadOnlyList<string>> ReadAgentNamesAsync(TestDatabase database)
	{
		return [.. (await new AgentService(database.ContextFactory, database.Clock).ListAsync(TestContext.Current.CancellationToken)).Select(agent => agent.Name)];
	}

	/// <summary>Reads the names of the project's categories.</summary>
	/// <param name="database">The database to read.</param>
	/// <returns>The names, in the order the category service lists them.</returns>
	public static async Task<IReadOnlyList<string>> ReadCategoryNamesAsync(TestDatabase database)
	{
		return [.. (await new CategoryService(database.ContextFactory).ListAsync(TestContext.Current.CancellationToken)).Select(category => category.Name)];
	}
}
