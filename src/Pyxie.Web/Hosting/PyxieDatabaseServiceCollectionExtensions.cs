using Pyxie.Data.Sqlite.DependencyInjection;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Web.Hosting;

/// <summary>Selects and registers the database provider named in configuration.</summary>
internal static class PyxieDatabaseServiceCollectionExtensions
{
	private const string ProviderKey = "Database:Provider";
	private const string SqliteProvider = "Sqlite";

	/// <summary>Registers the machine and project database factories for the provider in <c>Database:Provider</c>. Both database files are found from the workspace root (<see cref="WorkspaceLayout"/>): the machine database in the root, each project's database in its folder.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The application configuration.</param>
	/// <returns>The same collection, for chaining.</returns>
	/// <exception cref="InvalidOperationException">The provider is unknown.</exception>
	public static IServiceCollection AddPyxieDatabase(this IServiceCollection services, IConfiguration configuration)
	{
		string provider = configuration[ProviderKey] ?? SqliteProvider;

		if (string.Equals(provider, SqliteProvider, StringComparison.OrdinalIgnoreCase))
		{
			return services.AddPyxieSqlite(serviceProvider => serviceProvider.GetRequiredService<WorkspaceLayout>().MachineDatabasePath);
		}

		throw new InvalidOperationException($"Unknown database provider '{provider}' in '{ProviderKey}'. Supported providers: {SqliteProvider}.");
	}
}
