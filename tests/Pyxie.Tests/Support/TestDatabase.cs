using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Persistence;
using Pyxie.Data.Sqlite.Persistence;

namespace Pyxie.Tests.Support;

/// <summary>A private in-memory SQLite database with the Pyxie schema, created per test and disposed with it.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
	private readonly SqliteConnection _connection;
	private readonly ServiceProvider _provider;

	/// <summary>Initializes a new instance of the <see cref="TestDatabase"/> class and creates the schema and seed data.</summary>
	/// <param name="interceptors">Interceptors attached to every context created on this database.</param>
	public TestDatabase(params IInterceptor[] interceptors)
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();

		ServiceCollection services = new();
		services.AddDbContextFactory<ProjectDbContext>(options => PyxieSqlite.ConfigureProject(options.AddInterceptors(interceptors), _connection));
		_provider = services.BuildServiceProvider();
		ContextFactory = _provider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();

		using ProjectDbContext context = ContextFactory.CreateDbContext();
		context.Database.EnsureCreated();
	}

	/// <summary>Gets the clock the services under test, and the rows <see cref="TestData"/> arranges, are stamped with. It stands still until the test moves it.</summary>
	public TestClock Clock { get; } = new();

	/// <summary>Gets the factory services under test use to create short-lived contexts on this database.</summary>
	public IDbContextFactory<ProjectDbContext> ContextFactory { get; }

	/// <summary>Creates a context on this database; the caller disposes it.</summary>
	/// <returns>A new context.</returns>
	public ProjectDbContext CreateContext() => ContextFactory.CreateDbContext();

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _provider.DisposeAsync();
		await _connection.DisposeAsync();
	}
}
