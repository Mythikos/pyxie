using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Persistence;
using Pyxie.Data.Sqlite.Persistence;

namespace Pyxie.Tests.Support;

/// <summary>A private SQLite database in a temporary file, created per test and deleted with it. Unlike <see cref="TestDatabase"/>, whose contexts all share one connection, every context here opens its own connection, so concurrent service calls meet the database's real locking. It is for the few tests that prove a service is safe when callers truly run at once.</summary>
public sealed class RepositoryRaceDatabase : IAsyncDisposable
{
	private readonly string _path;
	private readonly ServiceProvider _provider;

	/// <summary>Initializes a new instance of the <see cref="RepositoryRaceDatabase"/> class and creates the schema.</summary>
	public RepositoryRaceDatabase()
	{
		_path = Path.Combine(Path.GetTempPath(), $"pyxie-race-{Guid.NewGuid():N}.sqlite3");

		// Configured as Pyxie configures a project database, whose connections are not pooled, so the file is released once the provider is disposed and can be deleted.
		ServiceCollection services = new();
		services.AddDbContextFactory<ProjectDbContext>(options => PyxieSqlite.ConfigureProject(options, PyxieSqlite.ConnectionString(_path)));
		_provider = services.BuildServiceProvider();
		ContextFactory = _provider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();

		using ProjectDbContext context = ContextFactory.CreateDbContext();
		context.Database.EnsureCreated();
	}

	/// <summary>Gets the factory services under test use to create short-lived contexts, each on its own connection.</summary>
	public IDbContextFactory<ProjectDbContext> ContextFactory { get; }

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _provider.DisposeAsync();

		// The journal exists only while a write is interrupted; the schema never switches the file to write-ahead logging.
		File.Delete(_path);
		File.Delete(_path + "-journal");
	}
}
