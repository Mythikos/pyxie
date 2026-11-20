using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Arranges and drives settings imports for the <see cref="SettingsTransferService"/> tests: the service on a test database with the stand-in project open, the file items the tests write, and the reads they check afterwards.</summary>
public static class SettingsTransferServiceTestData
{
	/// <summary>The identifier of the stand-in project that <see cref="StandInProject.Open"/> opens.</summary>
	public const int ProjectId = 1;

	/// <summary>The default sandbox Dockerfile as an export writes it while none was saved.</summary>
	public static string DefaultDockerfile => SandboxSettingsInputRules.NormalizeDockerfile(SandboxDefaults.Dockerfile);

	/// <summary>Gets every section, as a new set each time.</summary>
	public static IReadOnlySet<TransferSection> Every => new HashSet<TransferSection>(Enum.GetValues<TransferSection>());

	/// <summary>Creates the service on a test database with the stand-in project open.</summary>
	/// <param name="database">The database.</param>
	/// <returns>The service.</returns>
	public static SettingsTransferService Create(TestDatabase database)
	{
		return new SettingsTransferService(database.ContextSource, StandInProject.Open(), database.Clock);
	}

	/// <summary>Gets a set of the given sections.</summary>
	/// <param name="sections">The sections.</param>
	/// <returns>A new set.</returns>
	public static IReadOnlySet<TransferSection> Only(params TransferSection[] sections)
	{
		return new HashSet<TransferSection>(sections);
	}

	/// <summary>Gets a valid Dockerfile, from Pyxie's base image, that differs from the default and from any other line given.</summary>
	/// <param name="line">The line it runs after the base image.</param>
	/// <returns>The Dockerfile, normalized.</returns>
	public static string Dockerfile(string line)
	{
		return $"ARG PYXIE_BASE\nFROM ${{PYXIE_BASE}}\n{line}\n";
	}

	/// <summary>Gets an agent as a settings file holds it.</summary>
	/// <param name="name">The name.</param>
	/// <param name="harness">The harness; Claude Code unless given.</param>
	/// <param name="command">The command; <c>claude</c> unless given.</param>
	/// <param name="arguments">The arguments; none unless given.</param>
	/// <param name="canWork">Whether it can work; yes unless given.</param>
	/// <param name="canReview">Whether it can review; yes unless given.</param>
	/// <param name="canMerge">Whether it is the merge agent; no unless given.</param>
	/// <returns>The file's agent.</returns>
	public static AgentInput Agent(string name, HarnessKind harness = HarnessKind.ClaudeCode, string command = "claude", string arguments = "", bool canWork = true, bool canReview = true, bool canMerge = false)
	{
		return new AgentInput(name, harness, command, arguments, canWork, canReview, canMerge);
	}

	/// <summary>Gets a category as a settings file holds it.</summary>
	/// <param name="name">The name.</param>
	/// <param name="color">The color, written #RRGGBB.</param>
	/// <returns>The file's category.</returns>
	public static CategoryInput Category(string name, string color)
	{
		return new CategoryInput(name, HexColor.Parse(color));
	}

	/// <summary>Gets the seeded categories as a settings file holds them, so a file that keeps them leaves every card's category in place.</summary>
	/// <returns>Feature, Bug, and Chore in their seeded colors.</returns>
	public static List<CategoryInput> SeededCategories()
	{
		return [new CategoryInput("Feature", CategoryColors.Green), new CategoryInput("Bug", CategoryColors.Pink), new CategoryInput("Chore", CategoryColors.Neutral)];
	}

	/// <summary>Exports the stand-in project's settings.</summary>
	/// <param name="service">The service.</param>
	/// <returns>The settings.</returns>
	public static async Task<ProjectSettings> ExportAsync(SettingsTransferService service)
	{
		return ResultAssert.Succeeded(await service.ExportAsync(ProjectId, TestContext.Current.CancellationToken));
	}

	/// <summary>Previews an import into the stand-in project.</summary>
	/// <param name="service">The service.</param>
	/// <param name="settings">The file's settings.</param>
	/// <returns>The preview.</returns>
	public static async Task<SettingsImportPreview> PreviewAsync(SettingsTransferService service, ProjectSettings settings)
	{
		return ResultAssert.Succeeded(await service.PreviewAsync(ProjectId, settings, TestContext.Current.CancellationToken));
	}

	/// <summary>Previews an import into the stand-in project and imports the picked sections of it, as the import dialog does.</summary>
	/// <param name="service">The service.</param>
	/// <param name="settings">The file's settings.</param>
	/// <param name="sections">The sections to import.</param>
	/// <returns>The import's result.</returns>
	public static async Task<ServiceResult> PreviewAndImportAsync(SettingsTransferService service, ProjectSettings settings, IReadOnlySet<TransferSection> sections)
	{
		SettingsImportPreview preview = await PreviewAsync(service, settings);

		return await service.ImportAsync(settings, preview, sections, TestContext.Current.CancellationToken);
	}

	/// <summary>Reads every agent, untracked, by identifier.</summary>
	/// <param name="database">The database.</param>
	/// <returns>The agents.</returns>
	public static async Task<List<Agent>> ReadAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().OrderBy(agent => agent.Id).ToListAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads every category, untracked, by identifier.</summary>
	/// <param name="database">The database.</param>
	/// <returns>The categories.</returns>
	public static async Task<List<Category>> ReadCategoriesAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Categories.AsNoTracking().OrderBy(category => category.Id).ToListAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads the stored agent with a name, compared exactly.</summary>
	/// <param name="database">The database.</param>
	/// <param name="name">The name.</param>
	/// <returns>The agent, or <see langword="null"/> when none has the name.</returns>
	public static async Task<Agent?> FindAgentAsync(TestDatabase database, string name)
	{
		return (await ReadAgentsAsync(database)).SingleOrDefault(agent => string.Equals(agent.Name, name, StringComparison.Ordinal));
	}

	/// <summary>Counts the agents that have the merge role, by query.</summary>
	/// <param name="database">The database.</param>
	/// <returns>The number of merge agents.</returns>
	public static async Task<int> CountMergeAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.CountAsync(agent => agent.CanMerge, TestContext.Current.CancellationToken);
	}
}
