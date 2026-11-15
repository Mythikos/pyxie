using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Sqlite.Persistence;

namespace Pyxie.Data.Sqlite.DependencyInjection;

/// <summary>Registers the SQLite provider for Pyxie's machine database and project databases.</summary>
public static class PyxieSqliteServiceCollectionExtensions
{
	/// <summary>Registers a <see cref="MachineDbContext"/> factory on the machine database file, a <see cref="ProjectDbContext"/> factory on the open project's database file, and the <see cref="IProjectDatabase"/> that creates and upgrades project databases, all with this assembly's migrations.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="machineDatabasePath">Supplies the full path of the machine database file from the application's services; it is asked once, when the factory is first used.</param>
	/// <returns>The same collection, for chaining.</returns>
	public static IServiceCollection AddPyxieSqlite(this IServiceCollection services, Func<IServiceProvider, string> machineDatabasePath)
	{
		services.AddDbContextFactory<MachineDbContext>((provider, options) => PyxieSqlite.ConfigureMachine(options, PyxieSqlite.ConnectionString(machineDatabasePath(provider))));
		services.AddSingleton<IDbContextFactory<ProjectDbContext>, SqliteProjectDbContextFactory>();
		services.AddSingleton<IProjectDatabase, SqliteProjectDatabase>();
		return services;
	}
}
