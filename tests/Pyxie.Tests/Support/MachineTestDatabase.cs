using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Sqlite.Persistence;

namespace Pyxie.Tests.Support;

/// <summary>A private in-memory SQLite machine database, configured as Pyxie configures it (with the filtered index that keeps one project open), created per test and disposed with it, and the project catalog over it with its own <see cref="OpenProject"/>.</summary>
public sealed class MachineTestDatabase : IAsyncDisposable
{
	private readonly SqliteConnection _connection;
	private readonly ServiceProvider _provider;

	/// <summary>Initializes a new instance of the <see cref="MachineTestDatabase"/> class and creates the schema.</summary>
	public MachineTestDatabase()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();

		ServiceCollection services = new();
		services.AddDbContextFactory<MachineDbContext>(options => PyxieSqlite.ConfigureMachine(options, _connection));
		_provider = services.BuildServiceProvider();
		ContextFactory = _provider.GetRequiredService<IDbContextFactory<MachineDbContext>>();

		using MachineDbContext context = ContextFactory.CreateDbContext();
		context.Database.EnsureCreated();

		Catalog = new ProjectCatalog(ContextFactory, OpenProject, Clock);
	}

	/// <summary>Gets the clock the catalog stamps projects with. It stands still until the test moves it.</summary>
	public TestClock Clock { get; } = new();

	/// <summary>Gets the open project the catalog sets.</summary>
	public OpenProject OpenProject { get; } = new();

	/// <summary>Gets the factory that creates contexts on this database.</summary>
	public IDbContextFactory<MachineDbContext> ContextFactory { get; }

	/// <summary>Gets the catalog under test.</summary>
	public ProjectCatalog Catalog { get; }

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _provider.DisposeAsync();
		await _connection.DisposeAsync();
	}
}
