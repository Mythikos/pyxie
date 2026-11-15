using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Data.Sqlite.Persistence;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Opens private in-memory SQLite databases and applies the shipped project or machine migrations to them, so migration tests can inspect the schema they produce.</summary>
public static class MigrationDatabase
{
	/// <summary>Opens an empty in-memory database; the caller disposes the connection.</summary>
	/// <returns>The open connection.</returns>
	public static async Task<SqliteConnection> OpenEmptyAsync()
	{
		SqliteConnection connection = new("Data Source=:memory:");
		await connection.OpenAsync(TestContext.Current.CancellationToken);

		return connection;
	}

	/// <summary>Opens an in-memory database with every shipped project migration applied; the caller disposes the connection.</summary>
	/// <returns>The open connection.</returns>
	public static async Task<SqliteConnection> OpenMigratedAsync()
	{
		SqliteConnection connection = await OpenEmptyAsync();

		await using ProjectDbContext context = CreateContext(connection);
		await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

		return connection;
	}

	/// <summary>Opens an in-memory database with every shipped machine migration applied; the caller disposes the connection.</summary>
	/// <returns>The open connection.</returns>
	public static async Task<SqliteConnection> OpenMigratedMachineAsync()
	{
		SqliteConnection connection = await OpenEmptyAsync();

		await using MachineDbContext context = CreateMachineContext(connection);
		await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

		return connection;
	}

	/// <summary>Creates a project context configured as Pyxie configures it, with the shipped migrations, on an open connection.</summary>
	/// <param name="connection">The open connection.</param>
	/// <returns>A new context; the caller disposes it.</returns>
	public static ProjectDbContext CreateContext(SqliteConnection connection)
	{
		DbContextOptionsBuilder<ProjectDbContext> builder = new();
		PyxieSqlite.ConfigureProject(builder, connection);

		return new ProjectDbContext(builder.Options);
	}

	/// <summary>Creates a machine context configured as Pyxie configures it, with the shipped migrations, on an open connection.</summary>
	/// <param name="connection">The open connection.</param>
	/// <returns>A new context; the caller disposes it.</returns>
	public static MachineDbContext CreateMachineContext(SqliteConnection connection)
	{
		DbContextOptionsBuilder<MachineDbContext> builder = new();
		PyxieSqlite.ConfigureMachine(builder, connection);

		return new MachineDbContext(builder.Options);
	}

	/// <summary>Runs a statement that returns no rows.</summary>
	/// <param name="connection">The open connection.</param>
	/// <param name="sql">The statement.</param>
	/// <returns>A task that completes when the statement has run.</returns>
	public static async Task ExecuteAsync(SqliteConnection connection, string sql)
	{
		await using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads the first column of every row as text.</summary>
	/// <param name="connection">The open connection.</param>
	/// <param name="sql">The query.</param>
	/// <returns>The first-column values in row order.</returns>
	public static async Task<List<string>> ReadAsync(SqliteConnection connection, string sql)
	{
		await using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		List<string> values = [];
		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
		{
			values.Add(reader.GetString(0));
		}

		return values;
	}
}
