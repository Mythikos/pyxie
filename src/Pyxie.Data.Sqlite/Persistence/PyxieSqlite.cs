using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Configures Pyxie's contexts for SQLite. Every options builder for the machine or a project database goes through here, at run time, at design time, and in tests, so the migrations, the model, and the databases the tools and tests create always agree.</summary>
public static class PyxieSqlite
{
	private static readonly string s_migrationsAssembly = typeof(PyxieSqlite).Assembly.GetName().Name ?? throw new InvalidOperationException("The SQLite assembly has no name.");

	/// <summary>Builds the connection string of a database file. Connections are not pooled, so no file stays open once its context is disposed: a closed project's folder can be moved or deleted, and a failed import can delete the folder it created.</summary>
	/// <param name="databasePath">The full path of the database file.</param>
	/// <returns>The connection string.</returns>
	public static string ConnectionString(string databasePath)
	{
		return new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
	}

	/// <summary>Configures options for the machine database on a connection string.</summary>
	/// <param name="options">The options builder of a <c>MachineDbContext</c>.</param>
	/// <param name="connectionString">The connection string.</param>
	public static void ConfigureMachine(DbContextOptionsBuilder options, string connectionString)
	{
		options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(s_migrationsAssembly)).ReplaceService<IModelCustomizer, SqliteMachineModelCustomizer>();
	}

	/// <summary>Configures options for the machine database on an open connection, which the caller owns.</summary>
	/// <param name="options">The options builder of a <c>MachineDbContext</c>.</param>
	/// <param name="connection">The connection.</param>
	public static void ConfigureMachine(DbContextOptionsBuilder options, DbConnection connection)
	{
		options.UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(s_migrationsAssembly)).ReplaceService<IModelCustomizer, SqliteMachineModelCustomizer>();
	}

	/// <summary>Configures options for a project database on a connection string.</summary>
	/// <param name="options">The options builder of a <c>ProjectDbContext</c>.</param>
	/// <param name="connectionString">The connection string.</param>
	public static void ConfigureProject(DbContextOptionsBuilder options, string connectionString)
	{
		options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(s_migrationsAssembly)).ReplaceService<IModelCustomizer, SqliteProjectModelCustomizer>();
	}

	/// <summary>Configures options for a project database on an open connection, which the caller owns.</summary>
	/// <param name="options">The options builder of a <c>ProjectDbContext</c>.</param>
	/// <param name="connection">The connection.</param>
	public static void ConfigureProject(DbContextOptionsBuilder options, DbConnection connection)
	{
		options.UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(s_migrationsAssembly)).ReplaceService<IModelCustomizer, SqliteProjectModelCustomizer>();
	}
}
