using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Pyxie.Runtime.Gates;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Integration;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.DependencyInjection;

/// <summary>Registers the runtime: processes, git, workspaces, the round runners, project import, opening and closing, repository browsing, and the pipeline itself.</summary>
public static class PyxieRuntimeServiceCollectionExtensions
{
	/// <summary>Registers the runtime and its hosted pipeline, reading the <c>Pipeline</c> and <c>Workspace</c> configuration sections. The pipeline settings are checked when the host starts. The data services (<c>AddPyxieData</c>) and a database provider must be registered as well.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The application configuration.</param>
	/// <returns>The same collection, for chaining.</returns>
	public static IServiceCollection AddPyxieRuntime(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<PipelineOptions>().Bind(configuration.GetSection(PipelineOptions.SectionName)).Validate(options => options.Workers is >= 1 and <= 32 && options.PollIntervalSeconds is >= 1 and <= 300 && options.RoundTimeoutMinutes is >= 1 and <= 1440, $"'{PipelineOptions.SectionName}' settings are out of range: {nameof(PipelineOptions.Workers)} must be 1-32, {nameof(PipelineOptions.PollIntervalSeconds)} 1-300, and {nameof(PipelineOptions.RoundTimeoutMinutes)} 1-1440.").ValidateOnStart();

		services.AddOptions<WorkspaceOptions>().Bind(configuration.GetSection(WorkspaceOptions.SectionName));

		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<ProcessRunner>();
		services.AddSingleton<GitClient>();
		services.AddSingleton<WorkspaceLayout>();
		services.AddSingleton<RepositoryLock>();
		services.AddSingleton<ISandboxes, DockerSandboxes>();
		services.AddSingleton<ISandboxSignIns, SandboxSignIns>();
		services.AddSingleton<CardSandboxes>();
		services.AddSingleton<HarnessRunner>();
		services.AddSingleton<GateRunner>();
		services.AddSingleton<CardIntegrator>();
		services.AddSingleton<PipelineController>();
		services.AddSingleton<PipelineActivity>();
		services.AddSingleton<RepositoryImports>();
		services.AddScoped<RoundExecutor>();
		services.AddScoped<ProjectSetupService>();
		services.AddScoped<ProjectSessions>();
		services.AddScoped<RepositoryBrowser>();
		services.AddHostedService<PipelineOrchestrator>();

		return services;
	}
}
