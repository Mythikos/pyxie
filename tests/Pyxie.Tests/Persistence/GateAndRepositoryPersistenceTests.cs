using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

/// <summary>Gates and the connected repository: the rows the pipeline reads to decide whether a stage applies and where to work.</summary>
public sealed class GateAndRepositoryPersistenceTests
{
	[Fact]
	public void NewGate_IsEnabledWithTheDefaultTimeout()
	{
		Gate gate = new();

		Assert.True(gate.IsEnabled, "A new gate runs unless a person turns it off.");
		Assert.Equal(PyxieLimits.DefaultGateTimeoutMinutes, gate.TimeoutMinutes);
	}

	[Fact]
	public async Task Gate_StoresEveryFieldAndReadsItBack()
	{
		await using TestDatabase database = new();
		await TestData.AddGateAsync(database, "Build", isEnabled: true, command: "dotnet", arguments: "build -warnaserror");
		Gate added = await TestData.AddGateAsync(database, "Lint", isEnabled: false, command: "lint.exe", arguments: string.Empty);
		await using ProjectDbContext context = database.CreateContext();

		List<Gate> gates = await context.Gates.AsNoTracking().OrderBy(gate => gate.SortOrder).ToListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Build", "Lint"], gates.Select(gate => gate.Name));
		Assert.Equal([0, 1], gates.Select(gate => gate.SortOrder));
		Assert.Equal([true, false], gates.Select(gate => gate.IsEnabled));
		Assert.Equal("build -warnaserror", gates[0].Arguments);
		Assert.Equal(string.Empty, gates[1].Arguments);
		Assert.Equal(added.Id, gates[1].Id);
		Assert.All(gates, gate => Assert.Equal(PyxieLimits.DefaultGateTimeoutMinutes, gate.TimeoutMinutes));
	}

	[Fact]
	public async Task Database_RefusesTwoGatesWithTheSameName()
	{
		await using TestDatabase database = new();
		await TestData.AddGateAsync(database, "Tests");

		await Assert.ThrowsAsync<DbUpdateException>(() => TestData.AddGateAsync(database, "Tests"));
	}

	[Fact]
	public async Task Repository_StoresAFolderImportAndReadsItBackWithUtcTimes()
	{
		await using TestDatabase database = new();
		Repository added = TestData.ConnectRepository(database, targetBranch: "develop");
		await using ProjectDbContext context = database.CreateContext();

		Repository stored = await context.Repositories.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

		Assert.Equal(added.Id, stored.Id);
		Assert.Equal(TestData.RepositoryPath, stored.Path);
		Assert.Equal(RepositorySource.Folder, stored.Source);
		Assert.True(Path.IsPathFullyQualified(stored.Path), $"The path '{stored.Path}' must be absolute.");
		Assert.True(Path.IsPathFullyQualified(stored.Origin), $"The origin '{stored.Origin}' of a folder import must be a full path.");
		Assert.Equal("develop", stored.TargetBranch);
		Assert.Equal(database.Clock.UtcNow, stored.ImportedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.ImportedUtc.Kind);
	}

	[Fact]
	public async Task Repository_StoresAUrlImportWithItsOrigin()
	{
		await using TestDatabase database = new();
		await using (ProjectDbContext writer = database.CreateContext())
		{
			writer.Repositories.Add(new Repository
			{
				Source = RepositorySource.Url,
				Path = Path.Combine(Path.GetTempPath(), "pyxie-test-clone"),
				Origin = "https://example.com/team/app.git",
				TargetBranch = "main",
				ImportedUtc = database.Clock.UtcNow,
			});
			await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await using ProjectDbContext reader = database.CreateContext();
		Repository stored = await reader.Repositories.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

		Assert.Equal(RepositorySource.Url, stored.Source);
		Assert.Equal("https://example.com/team/app.git", stored.Origin);
	}

	[Fact]
	public async Task NewDatabase_HasNoRepositoryUntilOneIsConnected()
	{
		await using TestDatabase database = new();
		await using ProjectDbContext context = database.CreateContext();
		Assert.False(await context.Repositories.AnyAsync(TestContext.Current.CancellationToken), "No repository is connected at first.");

		TestData.ConnectRepository(database);
		Assert.True(await context.Repositories.AnyAsync(TestContext.Current.CancellationToken), "The repository is connected.");
	}
}
