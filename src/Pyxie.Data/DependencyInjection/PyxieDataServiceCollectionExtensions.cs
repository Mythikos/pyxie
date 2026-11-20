using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;

namespace Pyxie.Data.DependencyInjection;

/// <summary>Registers the Pyxie data services.</summary>
public static class PyxieDataServiceCollectionExtensions
{
	/// <summary>Registers the open project, the project catalog, the card, pipeline, category, agent, gate, repository, sandbox settings, and settings transfer services, and the system clock they stamp times with unless a <see cref="TimeProvider"/> is already registered. The provider must register the context factories for the machine database and for the open project's database, an <see cref="Persistence.IProjectDbContextSource"/>, and an <see cref="IProjectDatabase"/>.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The same collection, for chaining.</returns>
	public static IServiceCollection AddPyxieData(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<OpenProject>();
		services.AddScoped<IProjectCatalog, ProjectCatalog>();
		services.AddScoped<ICardService, CardService>();
		services.AddScoped<ICardMediaService, CardMediaService>();
		services.AddScoped<ICardPipelineService, CardPipelineService>();
		services.AddScoped<ICategoryService, CategoryService>();
		services.AddScoped<IAgentService, AgentService>();
		services.AddScoped<IGateService, GateService>();
		services.AddScoped<IRepositoryService, RepositoryService>();
		services.AddScoped<ISandboxSettingsService, SandboxSettingsService>();
		services.AddScoped<ISettingsTransferService, SettingsTransferService>();
		return services;
	}
}
