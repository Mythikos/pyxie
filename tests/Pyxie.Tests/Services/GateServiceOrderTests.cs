using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>The order gates run in is the order of their positions. Creating, deleting, and moving keep those positions consecutive from zero, and repair any gaps or ties that older writes left. Several tests arrange such gaps and ties directly, because <see cref="TestData.AddGateAsync"/> never produces them.</summary>
public sealed class GateServiceOrderTests
{
	[Fact]
	public async Task CreateAsync_PutsTheFirstGateAtPositionZero()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Build"), TestContext.Current.CancellationToken));

		Assert.Equal("0", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task CreateAsync_AppendsEachGateToTheBottom()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Build"), token));
		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Tests"), token));
		Gate last = ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Lint"), token));

		Assert.Equal("Build,Tests,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
		Assert.Equal(2, last.SortOrder);
	}

	[Fact]
	public async Task CreateAsync_GoesOnePastTheHighestPosition_NotPastTheCount()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "First", sortOrder: 0);
		await GateTestData.AddAtAsync(database, "Far", sortOrder: 5);
		GateService service = new(database.ContextFactory);

		Gate created = ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("New"), TestContext.Current.CancellationToken));

		Assert.Equal(6, created.SortOrder);
		Assert.Equal("First,Far,New", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task CreateAsync_AfterADeleteContinuesFromTheRemainingGates()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.DeleteAsync(gates["Lint"].Id, token));
		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Analysis"), token));

		Assert.Equal("Build,Tests,Analysis", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Theory]
	[InlineData("Build", "Tests,Lint,Analysis")]
	[InlineData("Tests", "Build,Lint,Analysis")]
	[InlineData("Lint", "Build,Tests,Analysis")]
	[InlineData("Analysis", "Build,Tests,Lint")]
	public async Task DeleteAsync_ClosesTheGapSoPositionsStayConsecutive(string deleted, string expectedOrder)
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint", "Analysis");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.DeleteAsync(gates[deleted].Id, TestContext.Current.CancellationToken));

		Assert.Equal(expectedOrder, await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_OfTheOnlyGateLeavesNone()
	{
		await using TestDatabase database = new();
		Gate gate = await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.DeleteAsync(gate.Id, TestContext.Current.CancellationToken));

		Assert.Empty(await GateTestData.ReadAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_RenumbersFromZeroOverGapsAndTiesLeftByOlderWrites()
	{
		await using TestDatabase database = new();
		Gate first = await GateTestData.AddAtAsync(database, "First", sortOrder: 0);
		await GateTestData.AddAtAsync(database, "Tied one", sortOrder: 5);
		await GateTestData.AddAtAsync(database, "Tied two", sortOrder: 5);
		await GateTestData.AddAtAsync(database, "Far", sortOrder: 9);
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.DeleteAsync(first.Id, TestContext.Current.CancellationToken));

		Assert.Equal("Tied one,Tied two,Far", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_KeepsTheGateWhenRenumberingTheOthersFails()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		CancellationToken token = TestContext.Current.CancellationToken;
		await using (ProjectDbContext context = database.CreateContext())
		{
			await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RefuseGateRenumbering BEFORE UPDATE ON Gates BEGIN SELECT RAISE(ABORT, 'Renumbering failed.'); END;", token);
		}

		GateService service = new(database.ContextFactory);

		await Assert.ThrowsAsync<DbUpdateException>(() => service.DeleteAsync(gates["Build"].Id, token));

		Assert.Equal("Build,Tests,Lint", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_OfAMissingGateWritesNothing()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "First", sortOrder: 2);
		await GateTestData.AddAtAsync(database, "Second", sortOrder: 7);
		GateService service = new(database.ContextFactory);

		ResultAssert.Failed(await service.DeleteAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("2,7", await GateTestData.ReadPositionsAsync(database));
	}

	[Theory]
	[InlineData("A", 0, "A,B,C,D")]
	[InlineData("A", 1, "B,A,C,D")]
	[InlineData("A", 2, "B,C,A,D")]
	[InlineData("A", 3, "B,C,D,A")]
	[InlineData("B", 0, "B,A,C,D")]
	[InlineData("B", 1, "A,B,C,D")]
	[InlineData("B", 2, "A,C,B,D")]
	[InlineData("B", 3, "A,C,D,B")]
	[InlineData("C", 0, "C,A,B,D")]
	[InlineData("C", 1, "A,C,B,D")]
	[InlineData("C", 2, "A,B,C,D")]
	[InlineData("D", 0, "D,A,B,C")]
	[InlineData("D", 2, "A,B,D,C")]
	[InlineData("D", 3, "A,B,C,D")]
	public async Task MoveAsync_PutsTheGateAtThePositionAndShiftsTheOthers(string moved, int position, string expectedOrder)
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "A", "B", "C", "D");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.MoveAsync(gates[moved].Id, position, TestContext.Current.CancellationToken));

		Assert.Equal(expectedOrder, await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2,3", await GateTestData.ReadPositionsAsync(database));
	}

	[Theory]
	[InlineData("A", -1, "A,B,C,D")]
	[InlineData("D", -1, "D,A,B,C")]
	[InlineData("C", int.MinValue, "C,A,B,D")]
	[InlineData("A", 4, "B,C,D,A")]
	[InlineData("B", 100, "A,C,D,B")]
	[InlineData("A", int.MaxValue, "B,C,D,A")]
	[InlineData("D", int.MaxValue, "A,B,C,D")]
	public async Task MoveAsync_ClampsAPositionOutsideTheGatesToTheFirstOrLast(string moved, int position, string expectedOrder)
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "A", "B", "C", "D");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.MoveAsync(gates[moved].Id, position, TestContext.Current.CancellationToken));

		Assert.Equal(expectedOrder, await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2,3", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task MoveAsync_OfTheOnlyGateKeepsItAtZero()
	{
		await using TestDatabase database = new();
		Gate gate = await GateTestData.AddAtAsync(database, "Tests", sortOrder: 4);
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.MoveAsync(gate.Id, 3, TestContext.Current.CancellationToken));

		Assert.Equal("0", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task MoveAsync_RenumbersFromZeroOverGapsAndTiesLeftByOlderWrites()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "First", sortOrder: 0);
		await GateTestData.AddAtAsync(database, "Tied one", sortOrder: 5);
		await GateTestData.AddAtAsync(database, "Tied two", sortOrder: 5);
		Gate far = await GateTestData.AddAtAsync(database, "Far", sortOrder: 9);
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.MoveAsync(far.Id, 1, TestContext.Current.CancellationToken));

		Assert.Equal("First,Far,Tied one,Tied two", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1,2,3", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task MoveAsync_RenumbersEvenWhenTheOrderDoesNotChange()
	{
		await using TestDatabase database = new();
		Gate first = await GateTestData.AddAtAsync(database, "First", sortOrder: 2);
		await GateTestData.AddAtAsync(database, "Second", sortOrder: 7);
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.MoveAsync(first.Id, 0, TestContext.Current.CancellationToken));

		Assert.Equal("First,Second", await GateTestData.ReadNamesAsync(database));
		Assert.Equal("0,1", await GateTestData.ReadPositionsAsync(database));
	}

	[Fact]
	public async Task MoveAsync_ChangesPositionsOnly()
	{
		await using TestDatabase database = new();
		await TestData.AddGateAsync(database, "Build", isEnabled: true, command: "msbuild", arguments: "-t:Rebuild");
		Gate disabled = await TestData.AddGateAsync(database, "Lint", isEnabled: false, command: "lint", arguments: "--fix");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.MoveAsync(disabled.Id, 0, TestContext.Current.CancellationToken));

		List<Gate> stored = await GateTestData.ReadAsync(database);
		Assert.Equal(["Lint", "Build"], stored.Select(gate => gate.Name));
		Assert.Equal(["lint", "msbuild"], stored.Select(gate => gate.Command));
		Assert.Equal(["--fix", "-t:Rebuild"], stored.Select(gate => gate.Arguments));
		Assert.Equal([false, true], stored.Select(gate => gate.IsEnabled));
	}

	[Fact]
	public async Task MoveAsync_ReportsAMissingGate_AndWritesNothing()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "First", sortOrder: 2);
		await GateTestData.AddAtAsync(database, "Second", sortOrder: 7);
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.MoveAsync(999, 0, TestContext.Current.CancellationToken));

		Assert.Equal("Gate not found.", error);
		Assert.Equal("2,7", await GateTestData.ReadPositionsAsync(database));
	}
}
