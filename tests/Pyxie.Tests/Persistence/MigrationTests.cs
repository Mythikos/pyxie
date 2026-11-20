using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

/// <summary>The shipped project migrations create the schema the model describes, with the constraints the services and the pipeline rely on.</summary>
public sealed class MigrationTests
{
	[Fact]
	public async Task ShippedMigrations_MatchTheModel()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();
		await using ProjectDbContext context = MigrationDatabase.CreateContext(connection);

		Assert.False(context.Database.HasPendingModelChanges(), "The migrations snapshot has drifted from the model; add a migration.");
		Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ShippedMigrations_SeedDefaultCategories()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();
		await using ProjectDbContext context = MigrationDatabase.CreateContext(connection);

		List<Category> categories = await context.Categories.OrderBy(category => category.Id).ToListAsync(TestContext.Current.CancellationToken);

		Assert.Equal([("Feature", CategoryColors.Green), ("Bug", CategoryColors.Pink), ("Chore", CategoryColors.Neutral)], categories.Select(category => (category.Name, category.Color)));
		Assert.False(await context.Agents.AnyAsync(TestContext.Current.CancellationToken), "No agents are seeded.");
		Assert.False(await context.Cards.AnyAsync(TestContext.Current.CancellationToken), "No cards are seeded.");
		Assert.False(await context.Rounds.AnyAsync(TestContext.Current.CancellationToken), "No rounds are seeded.");
		Assert.False(await context.Gates.AnyAsync(TestContext.Current.CancellationToken), "No gates are seeded.");
		Assert.False(await context.Repositories.AnyAsync(TestContext.Current.CancellationToken), "No repository is connected until the project's import connects it.");
	}

	[Theory]
	[InlineData("Categories", "Name")]
	[InlineData("Agents", "Name")]
	[InlineData("Gates", "Name")]
	public async Task ShippedMigrations_CreateUniqueNameIndexes(string table, string column)
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		// A filtered index (the one that allows a single merge agent) is not a constraint on every row, so it is looked at on its own.
		List<string> uniqueIndexes = await MigrationDatabase.ReadAsync(connection, $"SELECT name FROM pragma_index_list('{table}') WHERE \"unique\" = 1 AND partial = 0");

		Assert.Single(uniqueIndexes);
		Assert.Equal([column], await MigrationDatabase.ReadAsync(connection, $"SELECT name FROM pragma_index_info('{uniqueIndexes[0]}')"));
	}

	[Fact]
	public async Task ShippedMigrations_CreateAFilteredUniqueIndexOverTheMergeAgentAlone()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> filtered = await MigrationDatabase.ReadAsync(connection, "SELECT name FROM pragma_index_list('Agents') WHERE \"unique\" = 1 AND partial = 1");

		Assert.Equal(["IX_Agents_CanMerge"], filtered);
		Assert.Equal(["CanMerge"], await MigrationDatabase.ReadAsync(connection, "SELECT name FROM pragma_index_info('IX_Agents_CanMerge')"));
		string definition = Assert.Single(await MigrationDatabase.ReadAsync(connection, "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_Agents_CanMerge'"));
		Assert.Contains("WHERE \"CanMerge\" = 1", definition, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ShippedMigrations_GiveEveryCategoryARequiredColorWithoutADefault()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || type || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END || ':' || ifnull(dflt_value, 'no default') FROM pragma_table_info('Categories') WHERE name = 'Color'");

		Assert.Equal(["Color:TEXT:required:no default"], columns);
	}

	[Theory]
	[InlineData("#eaf3e2")]
	[InlineData("#EAF3E")]
	[InlineData("#EAF3E2F")]
	[InlineData("#EAF3E2FF")]
	[InlineData("EAF3E2")]
	[InlineData(" #EAF3E2")]
	[InlineData("#EAF3E2;color:red")]
	[InlineData("#GGGGGG")]
	[InlineData("red")]
	[InlineData("")]
	public async Task ShippedMigrations_RefuseACategoryColorNotWrittenAsHexColorWritesIt(string color)
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, $"INSERT INTO \"Categories\" (\"Name\", \"Color\") VALUES ('Docs', '{color}')"));
		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, $"UPDATE \"Categories\" SET \"Color\" = '{color}' WHERE \"Name\" = 'Bug'"));
	}

	[Fact]
	public async Task ShippedMigrations_AcceptACategoryColorWrittenAsHexColorWritesIt()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		await MigrationDatabase.ExecuteAsync(connection, "INSERT INTO \"Categories\" (\"Name\", \"Color\") VALUES ('Docs', '#0A1B2C')");

		Assert.Equal(["#0A1B2C"], await MigrationDatabase.ReadAsync(connection, "SELECT \"Color\" FROM \"Categories\" WHERE \"Name\" = 'Docs'"));
	}

	[Fact]
	public async Task ShippedMigrations_GiveEveryAgentARequiredMergeFlagWithoutADefault()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || type || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END || ':' || ifnull(dflt_value, 'no default') FROM pragma_table_info('Agents') WHERE name LIKE 'Can%' ORDER BY name");

		Assert.Equal(["CanMerge:INTEGER:required:no default", "CanReview:INTEGER:required:no default", "CanWork:INTEGER:required:no default"], columns);
	}

	[Fact]
	public async Task ShippedMigrations_RefuseASecondMergeAgentButKeepAnyNumberOfOtherAgents()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();
		await MigrationDatabase.ExecuteAsync(connection, InsertAgent("first", canMerge: false));
		await MigrationDatabase.ExecuteAsync(connection, InsertAgent("second", canMerge: false));
		await MigrationDatabase.ExecuteAsync(connection, InsertAgent("merger", canMerge: true));

		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, InsertAgent("another", canMerge: true)));
		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, "UPDATE \"Agents\" SET \"CanMerge\" = 1 WHERE \"Name\" = 'first'"));
		await MigrationDatabase.ExecuteAsync(connection, InsertAgent("third", canMerge: false));

		await MigrationDatabase.ExecuteAsync(connection, "UPDATE \"Agents\" SET \"CanMerge\" = 0 WHERE \"Name\" = 'merger'");
		await MigrationDatabase.ExecuteAsync(connection, "UPDATE \"Agents\" SET \"CanMerge\" = 1 WHERE \"Name\" = 'first'");
		Assert.Equal(["first"], await MigrationDatabase.ReadAsync(connection, "SELECT \"Name\" FROM \"Agents\" WHERE \"CanMerge\" = 1"));
	}

	[Fact]
	public async Task ShippedMigrations_CreateCardForeignKeysWithCategoryRestrictingAndAgentsSettingNull()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> foreignKeys = await MigrationDatabase.ReadAsync(connection, "SELECT \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('Cards') ORDER BY \"from\"");

		Assert.Equal(
			[
				"CategoryId->Categories:RESTRICT",
				"ReviewerAgentId->Agents:SET NULL",
				"WorkerAgentId->Agents:SET NULL",
			],
			foreignKeys);
	}

	[Fact]
	public async Task ShippedMigrations_RequireACategoryOnEveryCardWithoutADefault()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END || ':' || ifnull(dflt_value, 'no default') FROM pragma_table_info('Cards') ORDER BY name");

		Assert.Contains("CategoryId:required:no default", columns);
		Assert.Contains("Description:required:no default", columns);
		Assert.Contains("WorkerAgentId:optional:no default", columns);
		Assert.Contains("ReviewerAgentId:optional:no default", columns);
	}

	[Fact]
	public async Task ShippedMigrations_GiveEveryCardItsStateAttemptsAndAnOptionalResumeStageWithoutDefaults()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || type || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END || ':' || ifnull(dflt_value, 'no default') FROM pragma_table_info('Cards') ORDER BY name");

		Assert.Contains("State:INTEGER:required:no default", columns);
		Assert.Contains("AttemptLimit:INTEGER:required:no default", columns);
		Assert.Contains("Attempt:INTEGER:required:no default", columns);
		Assert.Contains("ResumeRound:INTEGER:optional:no default", columns);
	}

	[Fact]
	public async Task ShippedMigrations_RefuseACardWithoutAValidCategory()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, InsertCard("NULL")));
		await Assert.ThrowsAsync<SqliteException>(() => MigrationDatabase.ExecuteAsync(connection, InsertCard("999")));
		await MigrationDatabase.ExecuteAsync(connection, InsertCard("1"));
	}

	[Fact]
	public async Task ShippedMigrations_GiveEveryCardARequiredIntegerVersion()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || type || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END FROM pragma_table_info('Cards') WHERE name = 'Version'");

		Assert.Equal(["Version:INTEGER:required"], columns);
	}

	[Fact]
	public async Task ShippedMigrations_CreateTheRoundsTableWithItsColumns()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END FROM pragma_table_info('Rounds') ORDER BY name");

		Assert.Contains("Id:required", columns);
		Assert.Contains("CardId:required", columns);
		Assert.Contains("Sequence:required", columns);
		Assert.Contains("Kind:required", columns);
		Assert.Contains("Attempt:required", columns);
		Assert.Contains("StartedUtc:required", columns);
		Assert.Contains("EndedUtc:optional", columns);
		Assert.Contains("Outcome:optional", columns);
		Assert.Contains("Note:optional", columns);
		Assert.Contains("Report:optional", columns);
		Assert.Contains("Briefing:optional", columns);
	}

	[Fact]
	public async Task ShippedMigrations_CreateTheGatesTableWithItsColumns()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END FROM pragma_table_info('Gates') ORDER BY name");

		Assert.Equal(["Arguments:required", "Command:required", "Id:required", "IsEnabled:required", "Name:required", "SortOrder:required", "TimeoutMinutes:required"], columns);
	}

	[Fact]
	public async Task ShippedMigrations_CreateTheRepositoriesTableWithItsColumns()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END FROM pragma_table_info('Repositories') ORDER BY name");
		List<string> uniqueIndexes = await MigrationDatabase.ReadAsync(connection, "SELECT name FROM pragma_index_list('Repositories') WHERE \"unique\" = 1");

		Assert.Equal(["Id:required", "ImportedUtc:required", "Origin:required", "Path:required", "Source:required", "TargetBranch:required"], columns);
		Assert.Empty(uniqueIndexes);
	}

	[Fact]
	public async Task ShippedMigrations_CreateTheSandboxSettingsTableWithItsColumnsAndNoRow()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name || ':' || type || ':' || CASE \"notnull\" WHEN 1 THEN 'required' ELSE 'optional' END FROM pragma_table_info('SandboxSettings') ORDER BY name");
		List<string> rows = await MigrationDatabase.ReadAsync(connection, "SELECT \"Dockerfile\" FROM \"SandboxSettings\"");

		Assert.Equal(["Dockerfile:TEXT:required", "Id:INTEGER:required", "UpdatedUtc:TEXT:required", "Version:INTEGER:required"], columns);
		Assert.True(rows.Count == 0, "No sandbox settings are seeded: the defaults apply until a person saves the project's Dockerfile.");
	}

	[Fact]
	public async Task ShippedMigrations_StoreTheAgentSnapshotInTheRoundsOwnRowWithoutAForeignKeyToAgents()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> columns = await MigrationDatabase.ReadAsync(connection, "SELECT name FROM pragma_table_info('Rounds') WHERE name LIKE 'Agent%' ORDER BY name");

		Assert.Equal(["Agent_Arguments", "Agent_Authentication", "Agent_Command", "Agent_Harness", "Agent_Name"], columns);
		Assert.Empty(await MigrationDatabase.ReadAsync(connection, "SELECT \"from\" FROM pragma_foreign_key_list('Rounds') WHERE \"table\" = 'Agents'"));
	}

	[Fact]
	public async Task ShippedMigrations_CascadeRoundsFromTheirCard()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> foreignKeys = await MigrationDatabase.ReadAsync(connection, "SELECT \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('Rounds') ORDER BY \"from\"");

		Assert.Equal(["CardId->Cards:CASCADE"], foreignKeys);
	}

	[Fact]
	public async Task ShippedMigrations_CreateAUniqueIndexOnCardAndSequence()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();

		List<string> uniqueIndexes = await MigrationDatabase.ReadAsync(connection, "SELECT name FROM pragma_index_list('Rounds') WHERE \"unique\" = 1");

		Assert.Single(uniqueIndexes);
		Assert.Equal(["CardId", "Sequence"], await MigrationDatabase.ReadAsync(connection, $"SELECT name FROM pragma_index_info('{uniqueIndexes[0]}') ORDER BY seqno"));
	}

	[Fact]
	public async Task ShippedMigrations_RefuseASecondRoundWithTheSameSequenceAndARoundWithoutACard()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();
		await using ProjectDbContext context = MigrationDatabase.CreateContext(connection);
		CancellationToken token = TestContext.Current.CancellationToken;
		WorkCard card = new() { Name = "Card", Description = "Text", CategoryId = 1, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
		context.Cards.Add(card);
		await context.SaveChangesAsync(token);
		context.Rounds.Add(new CardRound { CardId = card.Id, Sequence = 1, Kind = RoundKind.Work, StartedUtc = DateTime.UtcNow });
		await context.SaveChangesAsync(token);

		context.Rounds.Add(new CardRound { CardId = card.Id, Sequence = 1, Kind = RoundKind.Gate, StartedUtc = DateTime.UtcNow });
		await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(token));
		context.ChangeTracker.Clear();
		context.Rounds.Add(new CardRound { CardId = card.Id + 100, Sequence = 1, Kind = RoundKind.Work, StartedUtc = DateTime.UtcNow });
		await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(token));
	}

	[Fact]
	public async Task ShippedMigrations_DeleteTheRoundsWhenTheirCardIsDeleted()
	{
		await using SqliteConnection connection = await MigrationDatabase.OpenMigratedAsync();
		await using ProjectDbContext context = MigrationDatabase.CreateContext(connection);
		CancellationToken token = TestContext.Current.CancellationToken;
		WorkCard card = new() { Name = "Card", Description = "Text", CategoryId = 1, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
		card.Rounds.Add(new CardRound { Sequence = 1, Kind = RoundKind.Work, StartedUtc = DateTime.UtcNow });
		card.Rounds.Add(new CardRound { Sequence = 2, Kind = RoundKind.Gate, StartedUtc = DateTime.UtcNow });
		context.Cards.Add(card);
		await context.SaveChangesAsync(token);
		Assert.Equal(2, await context.Rounds.CountAsync(token));

		await context.Cards.Where(existing => existing.Id == card.Id).ExecuteDeleteAsync(token);

		Assert.Equal(0, await context.Rounds.CountAsync(token));
	}

	private static string InsertAgent(string name, bool canMerge)
	{
		return $"INSERT INTO \"Agents\" (\"Name\", \"Harness\", \"Authentication\", \"Command\", \"Arguments\", \"CanWork\", \"CanReview\", \"CanMerge\") VALUES ('{name}', 0, 1, 'claude', '', 1, 1, {(canMerge ? 1 : 0)})";
	}

	private static string InsertCard(string categoryId)
	{
		return $"INSERT INTO \"Cards\" (\"Name\", \"Description\", \"Lane\", \"SortOrder\", \"State\", \"AttemptLimit\", \"Attempt\", \"Version\", \"CategoryId\", \"CreatedUtc\", \"UpdatedUtc\") VALUES ('Card', 'Text', 0, 0, 0, 3, 0, 0, {categoryId}, '2026-01-01 00:00:00', '2026-01-01 00:00:00')";
	}
}
