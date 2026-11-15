using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

/// <summary>The shipped machine migrations create the projects table the model describes, and the database itself keeps names and folders unique and at most one project open.</summary>
public sealed class MachineMigrationTests
{
	[Fact]
	public async Task ShippedMachineMigrations_MatchTheModel()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedMachineAsync();
		await using MachineDbContext context = MigrationDatabase.CreateMachineContext(connection);

		Assert.False(context.Database.HasPendingModelChanges(), "The machine migrations snapshot has drifted from the model; add a migration.");
		Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ShippedMachineMigrations_CreateOnlyTheProjectsTableWithItsColumns()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedMachineAsync();

		List<string> tables = await MigrationDatabase.ReadAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite%' AND name NOT LIKE '__EFMigrations%' ORDER BY name");
		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END FROM pragma_table_info('Projects') ORDER BY name");

		Assert.Equal(["Projects"], tables);
		Assert.Equal(["CreatedUtc:required", "Folder:required", "Id:required", "IsOpen:required", "Name:required", "Source:required"], columns);
	}

	[Fact]
	public async Task ShippedMachineMigrations_RefuseTwoProjectsWithTheSameNameOrFolder()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedMachineAsync();
		await MigrationDatabase.ExecuteAsync(connection, InsertProject("app", "C:/projects/app", isOpen: false));

		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, InsertProject("app", "C:/projects/other", isOpen: false)));
		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, InsertProject("other", "C:/projects/app", isOpen: false)));
	}

	[Fact]
	public async Task ShippedMachineMigrations_RefuseASecondOpenProjectButKeepAnyNumberClosed()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedMachineAsync();
		await MigrationDatabase.ExecuteAsync(connection, InsertProject("first", "C:/projects/first", isOpen: false));
		await MigrationDatabase.ExecuteAsync(connection, InsertProject("second", "C:/projects/second", isOpen: false));
		await MigrationDatabase.ExecuteAsync(connection, InsertProject("open", "C:/projects/open", isOpen: true));

		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, InsertProject("another", "C:/projects/another", isOpen: true)));
		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, "UPDATE \"Projects\" SET \"IsOpen\" = 1 WHERE \"Name\" = 'first'"));
	}

	private static string InsertProject(string name, string folder, bool isOpen)
	{
		return $"INSERT INTO \"Projects\" (\"Name\", \"Folder\", \"Source\", \"CreatedUtc\", \"IsOpen\") VALUES ('{name}', '{folder}', 'https://example.test/{name}.git', '2026-01-01 00:00:00', {(isOpen ? 1 : 0)})";
	}
}
